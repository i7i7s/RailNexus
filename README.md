# RailNexus

RailNexus adalah alat untuk mengelola aset Trainz. Tahap 0 (integrasi TrainzUtil, transaksi aman, pemulihan) sudah lolos; proyek kini berada di **Tahap 1**.

## Status

Gerbang Tahap 0 telah diuji pada TRS19 build 117026 ([`docs/tahap0.md`](docs/tahap0.md)). Seluruh layar inti (Tahap 1) dan sebagian Tahap 2 sudah dibangun dan diuji dengan simulator: Sambungkan, Pustaka, Mau edit apa?, ganti komponen, dependency, perbaikan, editor, Workshop Tekstur/PBR/Suara, batch, riwayat/undo, dan pengaturan ([`docs/tahap1.md`](docs/tahap1.md)). Aplikasi **belum siap pakai**: tampilan WPF belum pernah dijalankan, perilaku di Trainz asli belum diuji, dan penulisan masih dibatasi ke aset uji. Langkah yang harus dijalankan di Windows: [`docs/handoff-windows.md`](docs/handoff-windows.md). Panduan pengguna: [`docs/panduan.md`](docs/panduan.md). Visi dan rencana produk: [`PRD-RailNexus-v1.2.md`](PRD-RailNexus-v1.2.md).

## Persyaratan

- Windows (untuk menjalankan aplikasi dan integrasi; build dan uji unit juga berjalan di Linux)
- .NET 10 SDK
- TRS19 untuk pengujian integrasi langsung; jalankan Trainz dan aktifkan **Advanced Debug Tools** di Trainz Launcher

## Mulai

Jalankan pengujian:

```powershell
dotnet test RailNexus.slnx
```

Jalankan aplikasi desktop (Windows):

```powershell
dotnet run --project src/RailNexus.App
```

Buat paket mandiri Windows (runtime disertakan) ke `artifacts/RailNexus-win-x64`:

```powershell
dotnet publish src/RailNexus.App/RailNexus.App.csproj -p:PublishProfile=win-x64
```

Komponen pihak ketiga dan lisensinya: [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

Lihat perintah CLI:

```powershell
dotnet run --project src/RailNexus.Cli
```

Untuk probe TrainzUtil atau membuat aset uji, lihat [panduan Tahap 0](docs/tahap0.md). Perintah yang mengubah aset dibatasi pada aset uji; jangan gunakan aset penting untuk eksperimen.

## Struktur

- `src/RailNexus.Core` - model KUID, config, peran dan mesin modifikasi, transaksi, dependency/diagnosis, tekstur, audio, batch.
- `src/RailNexus.Trainz` - adapter TrainzUtil, profil build, serta klasifikasi keluaran.
- `src/RailNexus.Cli` - perintah untuk probe dan pengujian alur integrasi.
- `src/RailNexus.App.Core` - ViewModel aplikasi, lintas platform dan dapat diuji.
- `src/RailNexus.App` - aplikasi desktop WPF (XAML dan composition root).
- `tests/RailNexus.Tests` - pengujian unit dan simulator kegagalan.
- `fixtures/trainzutil/trs19` - contoh keluaran TrainzUtil.
