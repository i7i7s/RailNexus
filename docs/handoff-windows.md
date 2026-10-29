# Serah-terima: pekerjaan yang harus dilakukan di Windows dengan TRS19

Dokumen ini untuk agent (atau orang) yang bekerja di mesin Windows pengguna, tempat TRS19 dan
TrainzUtil terpasang. Semua kode di branch ini dibuat dan diuji di lingkungan Linux **tanpa Trainz**:
logika diuji dengan simulator (`tests/RailNexus.Tests/SimulatedTrainz.cs`), aplikasi WPF hanya
dikompilasi dan **belum pernah dijalankan atau dilihat**. Tugas di bawah menutup celah itu.

Baca dulu: `README.md`, `docs/tahap0.md` (temuan nyata TrainzUtil, wajib dipatuhi), `docs/tahap1.md`
(status dan keputusan), `docs/panduan.md` (alur pengguna), lalu PRD bagian 21, 23, 27.

## Aturan yang tidak boleh dilanggar

1. **Hanya aset uji yang ditulis.** Aset uji Tahap 0: `<kuid:-2:117104311>` (TRS19 build 117026). Aset
   uji baru harus dibuat dengan `make-test-asset` atau varian yang memuat penanda `RailNexus-uji-Tahap0`.
2. **`verified: true` di `src/RailNexus.Trainz/profiles/trs19.json` hanya dengan bukti**: keluaran nyata
   ditangkap ke `fixtures/trainzutil/trs19/` dengan `capture`, parser lolos uji terhadap fixture itu, dan
   kolom `evidence` menyebut nama fixture. Jangan mengarang atau menyunting isi fixture.
3. **Jangan menyimpan isi config, tekstur, atau suara milik pembuat mod lain ke repo.** Fixture hanya dari
   aset buatan sendiri; untuk aset orang lain cukup catat ringkasan/diagnosis (seperti `parse-check`).
4. **Jangan menandai peran "Otomatis teruji"** (`SupportLevel.AutomatedTested` di
   `src/RailNexus.Core/Roles/Roles.cs`) sebelum skenario T8 lolos untuk peran itu.
5. Exit code TrainzUtil selalu 0; hasil dibaca dari baris `OK (n Errors, m Warnings)`. Path berspasi
   ditolak `installfrompath`. Trainz harus berjalan dengan **Advanced Debug Tools** dan dibuka ulang.
6. Commit per tugas dengan pesan yang menyebut bukti. Uji (`dotnet test`) harus lolos sebelum push.

## Persiapan

```powershell
git fetch origin
git checkout claude/loving-tesla-dez5q9
dotnet --list-sdks   # butuh .NET 10 SDK
```

Jalankan Trainz (Advanced Debug Tools aktif, sudah dibuka ulang). Bila TrainzUtil tidak di lokasi Steam
standar, tambahkan `--trainz "<folder>"` pada perintah CLI.

Singkatan di bawah: `rn` = `dotnet run --project src/RailNexus.Cli --`.

---

## T1 — Uji otomatis di Windows

```powershell
dotnet test RailNexus.slnx
```

**Harapan:** semua lolos (212 uji per commit terakhir), termasuk
`TrainzUtilAdapterTests.Edit_folder_from_real_output_shape_must_exist` yang gagal di Linux karena
memerlukan path `C:/…`.
**Bila gagal:** perbaiki penyebabnya (biasanya perbedaan path/akhir baris Windows). Jangan melewati atau
menonaktifkan uji.

## T2 — Lihat aplikasi untuk pertama kali

```powershell
dotnet run --project src/RailNexus.App
```

Periksa setiap halaman terhadap kanvas desain https://claude.ai/artifact/D44DxCuuND51aHU3REbVuP pada
1440 × 900 dan 1280 × 720, lalu dengan skala teks Windows 125 % dan 150 % (AC-39):
Sambungkan, Pustaka, Mau edit apa?, Pilih pengganti, Editor (5 tab), Komponen & dependency, Periksa &
perbaiki, Perbaikan (daftar), Workshop, Workshop Tekstur, Perakit PBR, Workshop Suara, Produk & muatan,
Riwayat, Batch & preset, Pengaturan.

Daftar periksa:
- [ ] Aplikasi terbuka tanpa error XAML/binding (jalankan dari Visual Studio atau perhatikan jendela
      "Terjadi kesalahan yang belum tertangani").
- [ ] Navigasi kiri, bar konteks, panel Rencana perubahan (340 px), dan bar bawah tidak menutupi kontrol;
      tombol utama tidak terpotong.
- [ ] Setiap status tampil sebagai pill berteks (bukan warna saja); teks kartu nonaktif masih terbaca.
- [ ] Fokus keyboard terlihat; Tab mencapai semua tombol; Enter membuka aset di Pustaka.
- [ ] Font: IBM Plex Sans bila terpasang, Segoe UI bila tidak; `Typography.Capitals` pada judul kecil
      tidak merusak teks.
- [ ] TabControl editor dan ComboBox tampil wajar (memakai gaya bawaan WPF, belum ditata).

Perbaiki XAML yang bermasalah (`src/RailNexus.App/Views`, `Themes/Theme.xaml`). Logika ada di
`src/RailNexus.App.Core` dan sudah teruji; jangan memindahkan logika ke code-behind. Simpan tangkapan layar
di luar repo dan rangkum temuan di `docs/tahap1.md`.

## T3 — Sambungkan dan pengaturan

1. Di aplikasi: **Sambungkan → Periksa koneksi**. Harapan: "Build 117026", "Terhubung · dapat mengedit aset uji".
2. **Pengaturan → Aset uji yang boleh ditulis**: tambahkan `<kuid:-2:117104311>`, simpan, sambungkan ulang.
3. Uji kegagalan: tutup Trainz → Periksa koneksi harus menampilkan langkah Advanced Debug Tools.

## T4 — Verifikasi perintah baca baru (searchbycategory, printconfig)

Kedua perintah sudah ada di profil dengan `verified: false`; adapter mengembalikan `Unknown` sampai
diverifikasi, sehingga **Perbarui indeks** dan **Baca jenis kandidat** di aplikasi belum berfungsi.

```powershell
rn capture searchbycategory-VE-full --note "keluaran lengkap" -- searchbycategory VE
rn capture printconfig-installed-test -- printconfig "<kuid:-2:117104311>"
rn capture printconfig-not-local -- printconfig "<kuid:-2:117104311>"   # saat aset terbuka untuk diedit, bila error 'Asset is not local' muncul
```

- Bandingkan bentuknya dengan parser `src/RailNexus.Trainz/TrainzOutputParsers.cs`
  (baris hit `+ <kuid> : + #n : <kuid>`; dump di antara `+ Start of … dump` dan `+ End of … dump`).
- Tambahkan uji di `tests/RailNexus.Tests/IndexerTests.cs` yang memakai fixture baru (jumlah hit sama
  dengan baris "N : assets matching…").
- Analisis `printconfig-test-asset.json` ("Asset is not local"): kapan terjadi? Catat di `docs/tahap0.md`.
- Bila semua cocok: ubah `verified` menjadi `true` dengan `evidence` berisi nama fixture.
- Untuk `printconfig`, catat juga apakah teksnya sama dengan `config.txt` di folder edit (Tahap 0 menemukan
  Trainz menormalkan config). Aplikasi tidak memakai `printconfig` sebagai dasar hash transaksi.

## T5 — Pustaka dari aplikasi

Setelah T4: **Pustaka → kategori `TV, VE, CMP` → Perbarui indeks**. Harapan: progres per kategori, jumlah
aset bertambah, filter "Bermasalah" dan "Komponen belum tersedia" berisi jumlah yang masuk akal. Ukur waktu
(Tahap 0: ±1 dtk per 213 aset dengan filterbystatus). Cari nama dan tempel KUID (AC-35).

## T6 — Siklus aset uji lewat aplikasi

1. Buka aset uji dari Pustaka. Harapan: "Mau edit apa?" tampil; status aset di Content Manager kembali
   tanpa `E` (pembacaan memakai edit → revert).
2. **Editor lanjutan → Formulir → Deskripsi** ubah → **Simpan rencana** → **Terapkan & Selesai**.
   Harapan: *Berhasil*; validator tanpa masalah baru.
3. **Riwayat → Batalkan perubahan terpilih**. Harapan: *Berhasil*; config kembali identik byte (bandingkan
   dengan `rn cycle` di Tahap 0).
4. Editor teks penuh: **Teks → Sunting teks**, ubah satu baris komentar, simpan, terapkan, undo.

## T7 — Workshop tekstur di Trainz asli

```powershell
rn texture --kuid "<kuid:-2:117104311>" --src <png-uji.png> --name rn_uji --replace --no-undo
```

Lalu buka aset uji di Trainz (Preview Asset). Harapan (AC-16): tidak terbalik atas/bawah atau kiri/kanan,
warna benar, alpha benar untuk PNG ber-alpha. Ulangi dengan `--role parameter` dan PNG ber-alpha.
Undo dengan `rn undo <txid>`. Ulangi alurnya dari aplikasi (**Tekstur & material**, dan **Metadata gambar
di aset**). Bila TRS19 menolak TGA 24/32-bit tak terkompresi asal kiri bawah, catat pesan validasinya;
penulis ada di `src/RailNexus.Core/Texture/TgaCodec.cs`.

## T8 — Aset uji kendaraan dan peran P0/P1 (gerbang "Otomatis teruji")

Aset uji Tahap 0 ber-KIND `texture`, jadi tidak punya klakson/bogie. Perlu aset uji kendaraan:

1. Tambahkan varian di `src/RailNexus.Trainz/TestAssetFactory.cs` (mis. `make-test-asset --vehicle`)
   yang membuat `kind "traincar"` dengan `hornsound`, `enginesound`, `bogeys` (2 slot, `reversed 0/1`),
   satu `track-sound` bila strukturnya sudah dipastikan (lihat T11), `kuid-table` dengan satu nama simbolik,
   mesh minimal, dan penanda `RailNexus-uji-Tahap0`. Target KUID harus aset **bawaan** yang ada di
   instalasi (cari lewat Pustaka). Uji dengan `validate` sampai tidak faulty.
2. Impor (`rn import`), daftarkan di Pengaturan, buka di aplikasi.
3. Untuk setiap peran: klakson, suara mesin, bogie (per slot), suara roda-rel, lalu interior/pantograf/
   enginespec bila asetnya punya: pilih pengganti → Terapkan → validasi → uji di Trainz (suara terdengar,
   bogie benar orientasinya) → Undo.
4. Catat hasil per peran di `docs/tahap1.md`. Peran yang lolos semua langkah boleh dinaikkan ke
   `SupportLevel.AutomatedTested` dengan catatan bukti.

Kriteria yang ditutup: AC-01, 02, 03, 04, 26, 28, 33, 36 pada Trainz asli.

## T9 — Skenario kegagalan yang belum diuji di Trainz asli

Dari `docs/tahap0.md`: AC-09 (aset sudah terbuka di Content Manager), AC-10 (berkas diubah setelah draft
disusun — ubah config lewat Content Manager di antara "Gunakan pengganti" dan "Terapkan"), AC-14 (undo
setelah edit eksternal), AC-22 (ruang disk habis; sulit, boleh lewat volume kecil). Harapan sesuai pesan di
`TransactionRunner`. Catat transaksi dan hasil di tabel `docs/tahap0.md`.

## T10 — Dependency turunan (list-dependencies)

Fixture `list-dependencies-*.json` hanya berisi aset tanpa dependency, jadi bentuk barisnya belum diketahui
dan **belum ada parser**.

```powershell
rn capture list-dependencies-vehicle -- list-dependencies "<kuid aset uji kendaraan dari T8>"
```

Bila bentuknya jelas: tambahkan `TrainzOp.ListDependencies` (verified: false dulu), parser + uji di
`TrainzOutputParsers`, lalu sumber dependency untuk `DependencyGraph` di
`src/RailNexus.App.Core/ViewModels/DependencyViewModel.cs` (`KnownDependencies`). Ubah ke verified
hanya setelah uji lolos.

## T11 — Analisis parser yang tertunda

```powershell
rn parse-check --category TV --limit 60
rn parse-check --category VE --limit 60
```

`parse-check` kini menghitung juga peran P1. Tugas:
- **tracksound 0/60**: cari tahu nama/letak container track-sound pada kendaraan nyata (apakah di aset
  bogie/rel?). Perbaiki `TrackSoundRole` dengan config **sintetis** yang meniru struktur, bukan salinan.
- **`CFG-UNEXPECTED` pada baris berisi KUID saja**: catat pola barisnya (tanpa menyimpan isi), perbaiki
  parser di `src/RailNexus.Core/Config/ConfigDocument.cs` dengan fixture sintetis.
- **queues**: periksa nama tag nyata (`size`, `initial-count`, `initial-kuid`, `product-kinds`,
  `allowed-categories`, …) agar `QueueInspector` tidak salah memahami.

## T12 — Suara di Windows

1. Aset uji kendaraan (T8) dengan satu WAV lokal yang disebut soundscript.
2. **Berkas suara lokal → Dengarkan** (MediaPlayer). Harapan: terdengar, **Stop** menghentikan, pindah
   halaman menghentikan playback (AUDIO-AC).
3. Pilih WAV pengganti → Masukkan ke draft → Terapkan → dengar di Trainz → Undo (AC-29: config tidak berubah).
4. Klakson: **Dengarkan yang sekarang / pengganti** pada aset yang dapat dibaca.

## T13 — Paket instalasi

```powershell
dotnet publish src/RailNexus.App/RailNexus.App.csproj -p:PublishProfile=win-x64
```

Harapan: `artifacts/RailNexus-win-x64/RailNexus.exe` berjalan di mesin **tanpa .NET SDK**, folder
`profiles/trs19.json` ikut, data tersimpan di `%LOCALAPPDATA%\RailNexus`. Tambahkan folder `licenses/`
berisi teks lisensi sesuai `THIRD-PARTY-NOTICES.md`. Pertimbangkan installer (MSIX/Inno Setup) setelah ini.

## T14 — (Opsional) Sampel untuk Tahap 3

Ikuti `docs/tahap3-spike.md`: kumpulkan sampel `.texture`/`.im` dari aset buatan sendiri, di luar repo.

---

## Peta kode

| Bagian | Lokasi |
| --- | --- |
| Adapter TrainzUtil, profil, parser keluaran | `src/RailNexus.Trainz/` |
| Transaksi, jurnal, undo, rekonsiliasi | `src/RailNexus.Core/Tx/` |
| Config parser/editor, diff | `src/RailNexus.Core/Config/` |
| Peran, mesin modifikasi, draft | `src/RailNexus.Core/Roles/` |
| Dependency, diagnosis, aturan perbaikan | `src/RailNexus.Core/Dependencies/` |
| Tekstur (PNG/TGA, metadata, PBR) | `src/RailNexus.Core/Texture/` |
| Suara (WAV) | `src/RailNexus.Core/Audio/` |
| Batch/preset, muatan | `src/RailNexus.Core/Batch/`, `src/RailNexus.Core/Cargo/` |
| ViewModel semua layar | `src/RailNexus.App.Core/ViewModels/` |
| XAML, tema, pemutar WPF | `src/RailNexus.App/` |
| CLI uji integrasi | `src/RailNexus.Cli/Program.cs` |

## Laporan balik

Untuk setiap tugas: status (lolos / gagal / sebagian), bukti (id transaksi, nama fixture, tangkapan layar
di luar repo), perubahan kode, dan hal yang masih belum dipastikan. Perbarui tabel di `docs/tahap0.md` dan
`docs/tahap1.md`, lalu push ke branch kerja.
