# RailNexus

RailNexus adalah alat untuk mengelola aset Trainz. Proyek ini masih pada **Tahap 0**: membuktikan integrasi TrainzUtil, transaksi aman, dan pemulihan perubahan pada aset uji.

## Status

Gerbang Tahap 0 telah diuji pada TRS19 build 117026. Aplikasi saat ini berupa CLI untuk pengujian integrasi, bukan aplikasi desktop siap pakai. Rincian implementasi, batasan, dan bukti pengujian ada di [`docs/tahap0.md`](docs/tahap0.md); visi dan rencana produk ada di [`PRD-RailNexus-v1.2.md`](PRD-RailNexus-v1.2.md).

## Persyaratan

- Windows
- .NET 10 SDK
- TRS19 untuk pengujian integrasi langsung; jalankan Trainz dan aktifkan **Advanced Debug Tools** di Trainz Launcher

## Mulai

Jalankan pengujian:

```powershell
dotnet test RailNexus.slnx
```

Lihat perintah CLI:

```powershell
dotnet run --project src/RailNexus.Cli
```

Untuk probe TrainzUtil atau membuat aset uji, lihat [panduan Tahap 0](docs/tahap0.md). Perintah yang mengubah aset dibatasi pada aset uji; jangan gunakan aset penting untuk eksperimen.

## Struktur

- `src/RailNexus.Core` - model KUID, operasi, config, dan mesin transaksi.
- `src/RailNexus.Trainz` - adapter TrainzUtil, profil build, serta klasifikasi keluaran.
- `src/RailNexus.Cli` - perintah untuk probe dan pengujian alur integrasi.
- `tests/RailNexus.Tests` - pengujian unit dan simulator kegagalan.
- `fixtures/trainzutil/trs19` - contoh keluaran TrainzUtil.
