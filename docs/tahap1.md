# Tahap 1 — inti yang memudahkan (sedang berjalan)

Rujukan: PRD bagian 5, 6, 9–13, 27–29. Desain UI (RN-000) ada di kanvas
https://claude.ai/artifact/D44DxCuuND51aHU3REbVuP. Temuan nyata TrainzUtil ada di [`tahap0.md`](tahap0.md)
dan tetap berlaku; dokumen ini tidak mengubahnya.

Bagian yang dikerjakan di lingkungan tanpa Trainz (cloud, Linux). Semua yang berhubungan dengan
Trainz asli diuji dengan `SimulatedTrainz`, bukan TrainzUtil. Daftar pekerjaan yang **harus**
dijalankan di Windows dengan TRS19 ada di bagian [TODO lokal](#todo-lokal-windows--trs19).

## Isi

| Bagian | Isi |
| --- | --- |
| `src/RailNexus.Core/Texture` | RN-009 Workshop tekstur dasar: dekoder PNG, kodek TGA, `.texture.txt`, staging, verifikasi baca ulang |
| `src/RailNexus.App.Core` | ViewModel lintas platform (net10.0) untuk UI-01, UI-02, UI-03, panel Draft, dan pilih pengganti |
| `src/RailNexus.App` | Aplikasi WPF (net10.0-windows) — view XAML dan composition root saja |
| `railnexus texture` | Perintah CLI untuk menguji Workshop Tekstur pada aset uji di Trainz asli |

## RN-009 Workshop tekstur dasar

Alur (PRD 13.2–13.4): rencana nama → selesaikan benturan → salin sumber ke staging → dekode dari
salinan → tulis TGA ke staging → baca ulang dari disk dan bandingkan setiap piksel → buat/perbarui
`.texture.txt` → `EditPlan` untuk `TransactionRunner`, item `Draft`, atau ekspor folder.

Keputusan:

- **Tanpa library pihak ketiga.** Dekoder PNG ditulis sendiri di atas `System.IO.Compression.ZLibStream`
  (bagian dari .NET, lisensi MIT): CRC per chunk, 5 filter baris, tipe warna 0/2/3/4/6, kedalaman
  1–16 bit, palet + tRNS, kunci warna, Adam7. Kodek TGA juga ditulis sendiri. Tidak ada dependensi
  baru; `Microsoft.Data.Sqlite` (MIT) sudah ada sejak Tahap 0.
- **Format dari isi berkas**, bukan ekstensi. PNG dari tanda tangan; TGA dari header yang diperiksa
  ketat (TGA tidak punya tanda tangan). JPEG, BMP, DDS dikenali lalu dilaporkan belum didukung.
  `.texture` terkompilasi tidak dibaca (PRD 13.5, P2) dan tidak ditebak dari tanda tangannya.
- **Output TGA tak terkompresi, asal kiri bawah**: 24-bit tanpa alpha, 32-bit dengan 8 bit alpha di
  descriptor. Bentuk ini paling umum; penerimaannya oleh TRS19 **belum diuji** (lihat TODO).
- **Alpha per peran** (AC-16, AC-17): gambar warna/umum dengan alpha seluruhnya 255 → 24-bit dengan
  catatan; normal map dan parameter map **selalu** 32-bit bila sumber punya alpha (alpha adalah data);
  membuang alpha hanya lewat pilihan eksplisit `AlphaChoice.Discard` dan tercatat di hasil.
- **Tidak ada operasi warna.** gAMA/iCCP/sRGB diabaikan; nilai kanal disalin apa adanya. PNG 16-bit
  diturunkan ke 8-bit (byte atas) dengan catatan. Ukuran tidak pernah diubah; ukuran bukan pangkat
  dua hanya diberi catatan.
- **Benturan nama diselesaikan sebelum apa pun dibuat** (AC-18): nama ganda dari dua sumber, berkas
  yang sudah ada di tujuan, dan nama tidak sah. Pilihan: ganti nama (`OutputName`), lewati, atau
  ganti dengan backup (hanya untuk berkas yang sudah ada; backup dibuat transaksi).
- **Sumber tidak pernah ditulis.** Sumber disalin ke staging, hash salinan diperiksa, dan hash sumber
  diperiksa ulang setelah konversi. Satu gambar gagal tidak menggugurkan gambar lain (PRD 13.7).
- **`.texture.txt`**: `Primary=<nama>.tga`; `Alpha=<nama>_alpha.tga` hanya bila mask terpisah diberikan;
  `Tile=st` hanya bila belum ada key `Tile`. Metadata lama diperbarui baris demi baris: key tak dikenal,
  urutan, BOM, dan akhir baris dipertahankan; key ganda dianggap ambigu dan tidak diubah. `Alpha=` lama
  dipertahankan dan diberi peringatan bila berkasnya tidak ada.
- **Hasil** menjadi `FileChange` + hash dasar (`ToEditPlan`), `DraftItem.WriteFile` (`ToDraftItems`)
  sehingga tekstur dan perubahan config diterapkan dalam satu transaksi, atau `ExportToFolder` yang
  melaporkan "tersimpan, belum terpasang di Trainz" (AC-25). Draft berisi tekstur saja tidak menulis
  `config.txt`.

| Kriteria | Bukti otomatis (fixture piksel buatan sendiri) |
| --- | --- |
| AC-16 PNG alpha → TGA | `Png_with_alpha_becomes_32bit_tga_with_exact_channels_and_orientation_AC16`, `Tga_encoder_writes_bottom_left_bgra_layout` (posisi byte diperiksa tanpa dekoder sendiri) |
| AC-17 alpha parameter map | `Parameter_map_alpha_is_kept_even_when_opaque_AC17`, `Normal_map_values_are_not_color_corrected` |
| AC-18 nama sama | `Two_sources_with_same_output_name_block_until_resolved_AC18`, `Existing_file_needs_choice_and_replace_binds_base_hash` |
| AC-25 ekspor folder | `Export_to_folder_reports_saved_not_installed_AC25` |
| Satu transaksi + undo | `Texture_and_config_change_apply_in_one_transaction_and_undo` |

## Kerangka aplikasi

- `RailNexus.App.Core` tidak bergantung pada WPF: `ObservableObject`, `RelayCommand`, `AsyncCommand`.
  Semua logika layar ada di sini dan diuji dengan simulator (`AppViewModelTests`).
- `RailNexus.App` hanya berisi XAML, konverter warna, dialog folder, dan composition root.
  `EnableWindowsTargeting=true` sehingga dapat di-build di Linux; aplikasi hanya berjalan di Windows.
- Lokasi data sama dengan CLI (`%LOCALAPPDATA%\RailNexus\tahap0`): indeks dari `railnexus index` dan
  jurnal transaksi dipakai bersama. Pengaturan aplikasi di `%LOCALAPPDATA%\RailNexus\app-settings.json`.

| Layar | ViewModel | Isi |
| --- | --- | --- |
| UI-01 Sambungkan | `ConnectViewModel` | Instalasi terdeteksi + folder lain, pemeriksaan berlabel (Advanced Debug Tools, buka ulang Trainz), Hubungkan. Build yang tidak cocok dengan profil = hanya baca |
| UI-02 Pustaka | `LibraryViewModel` | Cari nama/KUID lewat `LibraryIndex`, chip status dengan jumlah (`LibraryIndex.Count(query)`), keadaan kosong dengan langkah berikut |
| UI-03 Mau edit apa? | `EditHubViewModel` | Tugas dari `RoleCatalog.Locate` pada config aset; tugas tak tersedia, config rusak, atau modul yang belum ada selalu disertai alasan |
| UI-04 (minimal) | `ReplaceViewModel` | Slot + konteks yang dipertahankan, kandidat dari pustaka dinilai `ModificationEngine.Evaluate`, tambahkan ke draft |
| Ringkasan perubahan | `DraftPanelViewModel` | Item draft, pratinjau `Draft.Build` (diurus otomatis / belum pasti), Terapkan & Selesai lewat `TransactionRunner`, hasil berlabel |

Batasan yang disengaja:

- **Membaca config memakai pola Tahap 0** (`edit` → baca `config.txt` → `revert`), karena byte yang
  dibaca harus sama dengan yang diverifikasi transaksi. `edit` adalah operasi tulis, sehingga di Trainz
  asli **hanya KUID di `writableTestKuids`** yang dapat dibuka di "Mau edit apa?". Aset lain mendapat
  pesan "bukan aset uji yang diizinkan". `printconfig` tidak dipakai karena belum ada di profil.
- **KIND kandidat belum ada di indeks** (kolom kategori `VE;LND` bukan KIND). Untuk peran yang
  mensyaratkan KIND (suara mesin, bogie) kandidat ditampilkan tetapi tidak dapat dipilih, dengan alasan.
  Klakson dan suara roda-rel memakai status "Jenis belum dapat dipastikan" sesuai `RoleCatalog`.
- Dependency, Perbaikan, Workshop, Riwayat: tampil di navigasi tetapi nonaktif dengan alasan.
  Panel Workshop Tekstur belum ada di aplikasi; mesinnya dapat diuji lewat `railnexus texture`.
- IBM Plex Sans (SIL OFL 1.1) **tidak dibundel**; aplikasi memakainya bila terpasang, bila tidak
  Segoe UI. Bila nanti dibundel, sertakan teks lisensi OFL.

**Tampilan WPF belum pernah dijalankan atau dilihat.** Yang terverifikasi hanya: XAML terkompilasi,
seluruh solusi ter-build, dan perilaku ViewModel lolos uji.

## Status uji (lingkungan cloud, Linux, .NET SDK 10.0.113)

`dotnet test RailNexus.slnx`: 157 dari 158 lolos. Yang gagal,
`TrainzUtilAdapterTests.Edit_folder_from_real_output_shape_must_exist`, adalah uji Tahap 0 yang
memerlukan path Windows (`C:/…`, sesuai bukti `edit-already-open.json`); di Linux folder sementara
berbentuk `/tmp/…` sehingga tidak cocok. Di Windows uji ini diharapkan lolos. Uji
`Path_escaping_asset_folder_is_rejected` yang sebelumnya juga gagal di Linux kini lolos karena
`SafePath` memperlakukan `\` sebagai pemisah di semua OS.

## TODO lokal (Windows + TRS19)

Tidak dapat dikerjakan di cloud karena membutuhkan Trainz/TrainzUtil. Jangan tandai perintah baru
sebagai `verified` di `profiles/trs19.json` sebelum ada keluaran nyata.

1. **Uji otomatis di Windows**: `dotnet test RailNexus.slnx` — harapan semua lolos, termasuk uji path Windows.
2. **Lihat aplikasi**: `dotnet run --project src/RailNexus.App`. Bandingkan dengan kanvas pada 1440 × 900
   dan 1280 × 720, juga dengan penskalaan teks Windows 125–150 % (AC-39). Catat panel yang terpotong,
   kontras teks pada kartu nonaktif, fokus keyboard, dan tombol yang kurang jelas. Pasang IBM Plex Sans bila ingin
   tampilan sesuai desain.
3. **Pengaturan**: jalankan aplikasi sekali, lalu isi `%LOCALAPPDATA%\RailNexus\app-settings.json`:
   `"WritableTestKuids": ["<kuid:-2:117104311>"]` (dan `"TrainzFolder"` bila Trainz tidak di lokasi umum).
4. **Alur aplikasi pada Trainz asli** (Advanced Debug Tools aktif, Trainz dibuka ulang):
   Sambungkan → Periksa koneksi (harus "Build 117026" dan "dapat mengedit aset uji") → Pustaka
   (isi dulu dengan `railnexus index --category TV,VE,CMP` bila kosong) → buka aset uji → pastikan
   status aset kembali tanpa `E` setelah dibaca (edit sudah di-revert). Aset uji tidak punya klakson,
   sehingga uji penggantian sungguhan memerlukan aset uji berkendaraan (lihat butir 7).
5. **Workshop tekstur pada aset uji**:
   ```bash
   dotnet run --project src/RailNexus.Cli -- texture --kuid "<kuid:-2:117104311>" --src <png-uji> --name rn_uji --replace --no-undo
   ```
   Periksa: transaksi `Succeeded` tanpa masalah validasi baru, "isi terpasang identik dengan hasil
   staging: True", lalu lihat teksturnya di Trainz (orientasi tidak terbalik, warna/alpha benar).
   Ulangi dengan PNG ber-alpha (32-bit) dan dengan `--role parameter`. Setelah itu `railnexus undo <txid>`.
   Bila TRS19 menolak bentuk TGA ini, catat pesan validasinya di `tahap0.md`/dokumen ini.
6. **Analisis parser yang tertunda** (dari `parse-check` Tahap 0): mengapa `tracksound` 0/60 pada
   kategori TV/VE (nama container berbeda, atau aturan berada di aset bogie/rel lain?), dan
   `CFG-UNEXPECTED` pada baris yang hanya berisi KUID. Jalankan `parse-check`, catat pola barisnya
   (tanpa menyimpan isi config milik pembuat lain), lalu perbaiki parser/peran dengan fixture sintetis.
7. **Aset uji kendaraan**: perluas `make-test-asset` (atau buat aset uji kedua) dengan `hornsound`,
   `enginesound`, `bogeys`, dan `kuid-table` agar penggantian peran P0 dapat diuji di Trainz asli
   sebelum status peran naik dari "Terpandu" menjadi "Otomatis teruji".
8. **KIND kandidat**: tentukan sumber KIND yang dapat dipercaya (mis. `printconfig` per kandidat atau
   perintah lain yang terbukti), tangkap keluarannya ke corpus, baru kemudian isi indeks dan aktifkan
   pemilihan kandidat suara mesin/bogie.
9. **Membaca aset non-uji tanpa `edit`**: bila `printconfig` akan dipakai sebagai jalur baca-saja di
   aplikasi, tambahkan ke profil sebagai `verified: false`, tangkap keluaran nyatanya, dan buktikan
   bahwa teksnya sama dengan `config.txt` di folder edit (Tahap 0 menemukan Trainz menormalkan config).
