using System.Globalization;
using Microsoft.Data.Sqlite;

namespace RailNexus.Core.Library;

/// <summary>Satu aset di indeks pustaka lokal (PRD 8, 24: AssetRecord).</summary>
public sealed record LibraryEntry(
    Kuid Kuid,
    string Name,
    string Category,
    string Flags,
    AssetState State,
    bool MissingDependencies,
    bool LocallyModified,
    bool Packaged,
    bool Payware,
    DateTimeOffset CheckedAt);

public enum LibraryFilter
{
    All,
    /// <summary>Aset bermasalah (faulty).</summary>
    Faulty,
    /// <summary>Komponen belum tersedia (missing dependency).</summary>
    MissingDependencies,
    /// <summary>Masih terbuka untuk diedit.</summary>
    OpenForEdit,
    /// <summary>Diubah secara lokal.</summary>
    LocallyModified,
}

public sealed record LibraryQuery(
    string Text = "",
    LibraryFilter Filter = LibraryFilter.All,
    /// <summary>Awalan kategori Trainz, mis. "TV" atau "CMP".</summary>
    string? Category = null,
    int Limit = 200,
    int Offset = 0);

/// <summary>
/// Indeks pustaka per profil instalasi di SQLite (PRD 23: indeks/cache lokal).
/// Profil berbeda tidak pernah tercampur, termasuk untuk KUID yang sama (CONNECT-01, AC-21).
/// </summary>
public sealed class LibraryIndex : IDisposable
{
    private readonly SqliteConnection _db;
    public string ProfileId { get; }

    public LibraryIndex(string databasePath, string profileId)
    {
        ProfileId = profileId;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        _db.Open();
        Exec("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS assets (
              profile      TEXT    NOT NULL,
              kuid         TEXT    NOT NULL,
              user_id      INTEGER NOT NULL,
              content_id   INTEGER NOT NULL,
              revision     INTEGER,
              name         TEXT    NOT NULL,
              name_key     TEXT    NOT NULL,
              category     TEXT    NOT NULL,
              flags        TEXT    NOT NULL,
              state        INTEGER NOT NULL,
              missing_deps INTEGER NOT NULL,
              modified     INTEGER NOT NULL,
              packaged     INTEGER NOT NULL,
              payware      INTEGER NOT NULL,
              checked_at   TEXT    NOT NULL,
              PRIMARY KEY (profile, kuid)
            );
            CREATE INDEX IF NOT EXISTS ix_assets_id ON assets(profile, user_id, content_id);
            CREATE INDEX IF NOT EXISTS ix_assets_name ON assets(profile, name_key, kuid);
            CREATE INDEX IF NOT EXISTS ix_assets_state ON assets(profile, state, name_key);
            CREATE INDEX IF NOT EXISTS ix_assets_missing ON assets(profile, missing_deps, name_key);
            CREATE INDEX IF NOT EXISTS ix_assets_modified ON assets(profile, modified, name_key);
            """);
    }

    private void Exec(string sql)
    {
        using var c = _db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    /// <summary>Menambah atau memperbarui catatan dalam satu transaksi database.</summary>
    public void Upsert(IEnumerable<LibraryEntry> entries)
    {
        using var tx = _db.BeginTransaction();
        using var c = _db.CreateCommand();
        c.Transaction = tx;
        c.CommandText = """
            INSERT INTO assets (profile, kuid, user_id, content_id, revision, name, name_key, category, flags, state,
                                missing_deps, modified, packaged, payware, checked_at)
            VALUES ($p, $k, $u, $c, $r, $n, $nk, $cat, $f, $s, $m, $l, $b, $pw, $t)
            ON CONFLICT(profile, kuid) DO UPDATE SET
              name = excluded.name, name_key = excluded.name_key, category = excluded.category, flags = excluded.flags, state = excluded.state,
              missing_deps = excluded.missing_deps, modified = excluded.modified, packaged = excluded.packaged,
              payware = excluded.payware, checked_at = excluded.checked_at;
            """;
        var p = new Dictionary<string, SqliteParameter>();
        foreach (var name in new[] { "$p", "$k", "$u", "$c", "$r", "$n", "$nk", "$cat", "$f", "$s", "$m", "$l", "$b", "$pw", "$t" })
            p[name] = c.Parameters.Add(new SqliteParameter { ParameterName = name });
        c.Prepare();
        foreach (var e in entries)
        {
            p["$p"].Value = ProfileId;
            p["$k"].Value = e.Kuid.ToString();
            p["$u"].Value = e.Kuid.UserId;
            p["$c"].Value = e.Kuid.ContentId;
            p["$r"].Value = (object?)e.Kuid.Revision ?? DBNull.Value;
            p["$n"].Value = e.Name;
            p["$nk"].Value = e.Name.ToLowerInvariant();
            p["$cat"].Value = e.Category;
            p["$f"].Value = e.Flags;
            p["$s"].Value = (int)e.State;
            p["$m"].Value = e.MissingDependencies ? 1 : 0;
            p["$l"].Value = e.LocallyModified ? 1 : 0;
            p["$b"].Value = e.Packaged ? 1 : 0;
            p["$pw"].Value = e.Payware ? 1 : 0;
            p["$t"].Value = e.CheckedAt.ToString("O", CultureInfo.InvariantCulture);
            c.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public long Count()
    {
        using var c = _db.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM assets WHERE profile = $p";
        c.Parameters.AddWithValue("$p", ProfileId);
        return (long)c.ExecuteScalar()!;
    }

    /// <summary>Jumlah aset yang cocok dengan teks dan filter (untuk label filter), tanpa memuat barisnya.</summary>
    public long Count(LibraryQuery q)
    {
        using var c = _db.CreateCommand();
        var (from, where, _) = BuildQuery(c, q);
        c.CommandText = $"SELECT COUNT(*) FROM {from} WHERE {where}";
        return (long)c.ExecuteScalar()!;
    }

    private (string From, string Where, string Order) BuildQuery(SqliteCommand c, LibraryQuery q)
    {
        var where = new List<string> { "profile = $p" };
        c.Parameters.AddWithValue("$p", ProfileId);

        var text = q.Text.Trim();
        // Pencarian KUID memakai indeks identitas; tanpa petunjuk ini SQLite menyusuri indeks nama
        // demi ORDER BY dan menjadi ~0,5 dtk pada 50.000 catatan (diukur di uji benchmark).
        var from = "assets";
        var order = "name_key, kuid";
        if (Kuid.TryParse(text, out var k))
        {
            from = "assets INDEXED BY ix_assets_id";
            order = "revision, kuid";
            where.Add("user_id = $u AND content_id = $c");
            c.Parameters.AddWithValue("$u", k.UserId);
            c.Parameters.AddWithValue("$c", k.ContentId);
        }
        else if (text.Length > 0)
        {
            where.Add("name_key LIKE $q ESCAPE '\\'");
            c.Parameters.AddWithValue("$q", "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }

        switch (q.Filter)
        {
            case LibraryFilter.Faulty: where.Add($"state = {(int)AssetState.Faulty}"); break;
            case LibraryFilter.OpenForEdit: where.Add($"state = {(int)AssetState.OpenForEdit}"); break;
            case LibraryFilter.MissingDependencies: where.Add("missing_deps = 1"); break;
            case LibraryFilter.LocallyModified: where.Add("modified = 1"); break;
        }
        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            where.Add("(category = $cat OR category LIKE $catp)");
            c.Parameters.AddWithValue("$cat", q.Category);
            c.Parameters.AddWithValue("$catp", q.Category + ";%");
        }

        return (from, string.Join(" AND ", where), order);
    }

    /// <summary>
    /// Pencarian dengan nama atau KUID. Teks yang berupa KUID mencari identitas aset
    /// (semua revisi); teks lain mencari bagian nama tanpa membedakan huruf besar/kecil.
    /// </summary>
    public IReadOnlyList<LibraryEntry> Search(LibraryQuery q)
    {
        using var c = _db.CreateCommand();
        var (from, where, order) = BuildQuery(c, q);

        c.CommandText = $"""
            SELECT kuid, name, category, flags, state, missing_deps, modified, packaged, payware, checked_at
            FROM {from} WHERE {where}
            ORDER BY {order} LIMIT $lim OFFSET $off
            """;
        c.Parameters.AddWithValue("$lim", q.Limit);
        c.Parameters.AddWithValue("$off", q.Offset);

        var result = new List<LibraryEntry>();
        using var r = c.ExecuteReader();
        while (r.Read())
        {
            result.Add(new LibraryEntry(
                Kuid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3),
                (AssetState)r.GetInt32(4), r.GetInt32(5) == 1, r.GetInt32(6) == 1, r.GetInt32(7) == 1, r.GetInt32(8) == 1,
                DateTimeOffset.Parse(r.GetString(9), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    public LibraryEntry? Get(Kuid kuid) =>
        Search(new LibraryQuery(kuid.ToString(), Limit: 50)).FirstOrDefault(e => e.Kuid == kuid);

    public void Dispose() => _db.Dispose();
}
