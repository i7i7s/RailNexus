# RailNexus

RailNexus adalah alat untuk mengelola aset Trainz. Tahap 0 (integrasi TrainzUtil, transaksi aman, pemulihan) sudah lolos; proyek kini berada di **Tahap 1**.

## Status

Gerbang Tahap 0 telah diuji pada TRS19 build 117026 ([`docs/tahap0.md`](docs/tahap0.md)). Tahap 1 berisi Workshop Tekstur dasar dan kerangka aplikasi desktop WPF (Sambungkan, Pustaka, "Mau edit apa?", ringkasan perubahan); lihat [`docs/tahap1.md`](docs/tahap1.md), termasuk daftar uji yang masih harus dijalankan di Windows dengan Trainz. Aplikasi desktop belum siap pakai: penulisan masih dibatasi ke aset uji. Visi dan rencana produk ada di [`PRD-RailNexus-v1.2.md`](PRD-RailNexus-v1.2.md).

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

Lihat perintah CLI:

```powershell
dotnet run --project src/RailNexus.Cli
```

Untuk probe TrainzUtil atau membuat aset uji, lihat [panduan Tahap 0](docs/tahap0.md). Perintah yang mengubah aset dibatasi pada aset uji; jangan gunakan aset penting untuk eksperimen.

## Struktur

- `src/RailNexus.Core` - model KUID, operasi, config, mesin transaksi, dan Workshop Tekstur (`Texture/`).
- `src/RailNexus.Trainz` - adapter TrainzUtil, profil build, serta klasifikasi keluaran.
- `src/RailNexus.Cli` - perintah untuk probe dan pengujian alur integrasi.
- `src/RailNexus.App.Core` - ViewModel aplikasi, lintas platform dan dapat diuji.
- `src/RailNexus.App` - aplikasi desktop WPF (XAML dan composition root).
- `tests/RailNexus.Tests` - pengujian unit dan simulator kegagalan.
- `fixtures/trainzutil/trs19` - contoh keluaran TrainzUtil.
