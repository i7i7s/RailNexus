# Komponen pihak ketiga

Daftar komponen yang ikut terdistribusi dalam paket aplikasi RailNexus (`dotnet publish` profil
`win-x64`). Lisensi dibaca dari metadata paket NuGet yang dipakai build (berkas `.nuspec`), bukan
dari ingatan. Perbarui daftar ini setiap kali dependensi berubah:

```bash
dotnet list src/RailNexus.App/RailNexus.App.csproj package --include-transitive
```

| Komponen | Versi | Lisensi | Keterangan |
| --- | --- | --- | --- |
| .NET Runtime (`Microsoft.NETCore.App`) | 10.0 | MIT | Disertakan pada paket mandiri |
| Windows Desktop Runtime / WPF (`Microsoft.WindowsDesktop.App`) | 10.0 | MIT | Disertakan pada paket mandiri |
| Microsoft.Data.Sqlite, Microsoft.Data.Sqlite.Core | 10.0.14 | MIT | Indeks pustaka |
| SQLitePCLRaw.bundle_e_sqlite3, .core, .lib.e_sqlite3, .provider.e_sqlite3 | 2.1.13 | Apache-2.0 | Pembungkus SQLite native |
| SQLite (`e_sqlite3`) | — | Domain publik | Mesin database, dibangun oleh SQLitePCLRaw |

Teks lisensi: MIT — https://licenses.nuget.org/MIT · Apache-2.0 — https://licenses.nuget.org/Apache-2.0 ·
SQLite — https://www.sqlite.org/copyright.html. Bila paket instalasi dibuat, sertakan teks lengkap
lisensi tersebut di folder `licenses/` paket.

## Bukan bagian dari paket

- **Paket uji** (xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector) hanya
  dipakai saat `dotnet test` dan tidak didistribusikan.
- **IBM Plex Sans / IBM Plex Mono** (SIL Open Font License 1.1) tidak dibundel; aplikasi memakainya bila
  terpasang di Windows. Bila nanti dibundel, sertakan teks OFL 1.1.
- **Trainz / TrainzUtil** milik N3V Games tidak disertakan; RailNexus memanggil TrainzUtil yang sudah
  terpasang di komputer pengguna.

## Kode yang ditulis sendiri

Dekoder PNG, kodek TGA, parser `.texture.txt`, parser WAV, perakit kanal, dan parser config Trainz
ditulis untuk RailNexus tanpa menyalin kode pihak lain. Dekompresi PNG memakai
`System.IO.Compression.ZLibStream` dari .NET (MIT). Utilitas komunitas yang disebut di PRD bagian 19
(AssetX, Images2TGA, dan lainnya) hanya menjadi referensi kebutuhan; kodenya tidak dipakai.
