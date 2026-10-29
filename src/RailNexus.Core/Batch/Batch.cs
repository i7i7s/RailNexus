using System.Text.Json;
using RailNexus.Core.Config;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.Core.Batch;

/// <summary>
/// Preset penggantian (PRD 20): maksud perubahan berdasarkan peran dan lokasi, bukan replace token KUID
/// global. Hanya identitas yang disimpan; tidak membawa berkas mod pihak lain.
/// </summary>
public sealed record ReplacePreset(string Name, string RoleId, string From, string To, int Version = ReplacePreset.CurrentVersion)
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Kuid FromKuid => Kuid.Parse(From);
    public Kuid ToKuid => Kuid.Parse(To);

    /// <summary>Alasan preset tidak dapat dipakai; null bila valid.</summary>
    public string? Problem =>
        RoleCatalog.ById(RoleId) is null ? $"Peran '{RoleId}' tidak dikenal."
        : !Kuid.TryParse(From, out var f) ? "KUID yang diganti tidak valid."
        : !Kuid.TryParse(To, out var t) ? "KUID pengganti tidak valid."
        : References.SameAsset(f, t) ? "Pengganti sama dengan yang diganti."
        : Version > CurrentVersion ? $"Preset dibuat versi aturan {Version}; aplikasi ini baru mengenal versi {CurrentVersion}."
        : null;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public static ReplacePreset? Load(string path)
    {
        try { return JsonSerializer.Deserialize<ReplacePreset>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }
}

public enum BatchItemState
{
    Ready,
    /// <summary>Butuh keputusan pengguna (mis. pengganti belum tersedia di profil ini).</summary>
    NeedsChoice,
    Skipped,
}

public sealed record BatchPlanItem(Kuid Asset, string Name, BatchItemState State, string Detail, EditPlan? Plan);

public static class BatchPlanner
{
    /// <param name="config">Byte config.txt dari isi terpasang; null bila tidak dapat dibaca.</param>
    /// <param name="replacementAvailable">Pengganti terpasang dan sehat di profil ini.</param>
    public static BatchPlanItem PlanReplace(Kuid asset, string name, byte[]? config, string? readError, ReplacePreset preset, bool replacementAvailable)
    {
        if (preset.Problem is { } p) return new(asset, name, BatchItemState.Skipped, p, null);
        if (config is null) return new(asset, name, BatchItemState.Skipped, $"Isi aset tidak dapat dibaca: {readError}", null);
        var role = RoleCatalog.ById(preset.RoleId)!;
        var text = new System.Text.UTF8Encoding(false).GetString(config).TrimStart('﻿');
        var doc = ConfigDocument.Parse(text);
        if (doc.HasErrors) return new(asset, name, BatchItemState.Skipped, "Config memiliki error struktur; dilewati tanpa perubahan.", null);

        // Hanya lokasi peran ini yang memakai aset yang diganti (PRD 20 langkah 2).
        var slots = role.Locate(doc).Where(s => References.SameAsset(s.Current, preset.FromKuid)).ToList();
        if (slots.Count == 0) return new(asset, name, BatchItemState.Skipped, $"Tidak memakai {preset.From} pada peran {role.Label}.", null);
        if (!replacementAvailable)
            return new(asset, name, BatchItemState.NeedsChoice, $"Pengganti {preset.To} belum tersedia atau bermasalah di profil ini; pilih pengganti lain.", null);

        var draft = new Draft(asset);
        foreach (var s in slots)
            draft.Put(new DraftItem.ReplaceKuid(role.Id, s.Path, s.Current, preset.ToKuid, "Batch", $"{role.Label} · {s.Label}: {s.Current} → {preset.To}"));
        var (plan, build) = draft.ToEditPlan(config, $"Batch '{preset.Name}': {role.Label} {preset.From} → {preset.To}");
        return plan is null
            ? new(asset, name, BatchItemState.Skipped, build.Error, null)
            : new(asset, name, BatchItemState.Ready, $"{slots.Count} lokasi: {string.Join(", ", slots.Select(s => s.Path))}" +
                  (build.Uncertain.Count > 0 ? $" · {string.Join(" ", build.Uncertain)}" : ""), plan);
    }
}

public sealed record BatchResultItem(Kuid Asset, string Name, BatchItemState Planned, TxResult? Result, string Message);

/// <summary>
/// Menjalankan rencana per aset satu per satu untuk satu profil (PRD 20 langkah 5). Bukan transaksi atomik:
/// kegagalan satu aset tidak membatalkan aset lain, dan keberhasilan sebelumnya tetap tercatat (AC-15).
/// Penghentian hanya terjadi di antara aset, setelah transaksi aset sebelumnya selesai.
/// </summary>
public sealed class BatchRunner(TransactionRunner runner)
{
    public async Task<IReadOnlyList<BatchResultItem>> RunAsync(IReadOnlyList<BatchPlanItem> items, IProgress<BatchResultItem>? progress = null, CancellationToken stop = default)
    {
        var results = new List<BatchResultItem>();
        foreach (var item in items)
        {
            BatchResultItem r;
            if (item.State != BatchItemState.Ready || item.Plan is null)
                r = new BatchResultItem(item.Asset, item.Name, item.State, null, item.Detail);
            else if (stop.IsCancellationRequested)
                r = new BatchResultItem(item.Asset, item.Name, item.State, null, "Tidak dijalankan: antrean dihentikan pengguna.");
            else
            {
                try
                {
                    var tx = await runner.ApplyAsync(item.Plan, CancellationToken.None).ConfigureAwait(false);
                    r = new BatchResultItem(item.Asset, item.Name, item.State, tx, tx.Message);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    r = new BatchResultItem(item.Asset, item.Name, item.State, null, $"Gagal sebelum transaksi selesai: {ex.Message}");
                }
            }
            results.Add(r);
            progress?.Report(r);
        }
        return results;
    }
}
