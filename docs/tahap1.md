# Tahap 1 dan 2 — status pengembangan

Tahap 1 (inti) dan bagian Tahap 2 yang tidak membutuhkan corpus Trainz nyata sudah dibangun. Semuanya
baru diuji dengan simulator; gerbang tahap baru lolos setelah tugas di
[`handoff-windows.md`](handoff-windows.md) dijalankan di TRS19 asli.

Rujukan: PRD bagian 5, 6, 9–13, 27–29. Desain UI (RN-000) ada di kanvas
https://claude.ai/artifact/D44DxCuuND51aHU3REbVuP. Temuan nyata TrainzUtil ada di [`tahap0.md`](tahap0.md)
dan tetap berlaku; dokumen ini tidak mengubahnya.

Bagian yang dikerjakan di lingkungan tanpa Trainz (cloud, Linux). Semua yang berhubungan dengan
Trainz asli diuji dengan `SimulatedTrainz`, bukan TrainzUtil. Pekerjaan yang **harus** dijalankan di
Windows dengan TRS19 ada di [`handoff-windows.md`](handoff-windows.md). Panduan pengguna:
[`panduan.md`](panduan.md). Format Tahap 3: [`tahap3-spike.md`](tahap3-spike.md).

## Isi

| Modul | Layar | Kode | Tahap |
| --- | --- | --- | --- |
| Sambungkan | UI-01 | `ConnectViewModel`, `TrainzConnectionService` | P0 |
| Pustaka + pengindeksan | UI-02 | `LibraryViewModel`, `TrainzLibraryIndexer` | P0 |
| Mau edit apa? | UI-03 | `EditHubViewModel` | P0 |
| Ganti komponen (P0 + P1) + preview suara A/B | UI-04/05/06/07/08 | `ReplaceViewModel`, `RoleCatalog.All` | P0 / P1 |
| Komponen & dependency (graf turunan) | UI-04, UI-11 inspeksi | `DependencyViewModel`, `Core/Dependencies` | P0 |
| Produk & muatan | UI-10 | `CargoViewModel`, `QueueInspector` | P1 inspeksi |
| Editor aset (formulir, struktur, teks yang dapat disunting, diff) | UI-12 | `EditorViewModel`, `LineDiff` | P0 terbatas · P1 teks |
| Periksa & perbaiki | UI-13 | `RepairViewModel`, `Diagnosis`, `RepairRules` | P0 dasar |
| Workshop Tekstur (konversi, metadata) | UI-14/15 | `TextureViewModel`, `Core/Texture` | P0 |
| Perakit PBR | UI-16 | `PbrViewModel`, `ChannelPacker` | P1 |
| Workshop Suara (WAV lokal) | UI-17 | `SoundViewModel`, `WavInfo` | P0 preview · P1 ganti WAV |
| Batch & preset | UI-22 | `BatchViewModel`, `Core/Batch` | P1 |
| Ringkasan, proses, hasil | UI-23 | `DraftPanelViewModel` | P0 |
| Riwayat & pemulihan | UI-24 | `HistoryViewModel` | P0 |
| Pengaturan | UI-25 | `SettingsViewModel` | P0 |
| Workshop (peta sembilan utilitas) | AC-34 | `WorkshopViewModel`, `ModuleCatalog` | P0 |
| CLI `texture` | — | `src/RailNexus.Cli/Program.cs` | uji integrasi |

**Belum dibuat** (dan alasannya): penambahan aturan tracksound baru dan diagnosis konfliknya (butuh corpus
nyata, lihat T11), penggantian referensi mesh library dan otomasi texture-group/script (P1, butuh corpus;
saat ini tampil sebagai "Referensi lain"), integrasi editor eksternal (P1), diagnosis Gerak & animasi (P1),
semua kemampuan format Tahap 3 (lihat `tahap3-spike.md`).

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

Daftar layar ada di bagian [Isi](#isi). Navigasi kiri: Pustaka dan Perbaikan aktif setelah terhubung,
Dependency setelah aset dibuka, Workshop sejak awal (mode folder tidak butuh Trainz), Riwayat setelah
terhubung, Pengaturan selalu.

Batasan yang disengaja:

- **Isi aset dibaca dengan pola Tahap 0** (`edit` → salin isi ke snapshot → `revert`), karena byte yang
  dibaca harus sama dengan yang diverifikasi transaksi. `edit` adalah operasi tulis, sehingga di Trainz
  asli **hanya KUID di `WritableTestKuids`** (Pengaturan) yang dapat dibuka di "Mau edit apa?". Aset lain
  mendapat pesan "bukan aset uji yang diizinkan". Snapshot menyimpan hash semua berkas dan isi sampai
  64 MB (berkas teks selalu).
- **KIND kandidat** baru diketahui setelah "Baca jenis kandidat" (printconfig). Selama `printconfig`
  belum terverifikasi, peran yang mensyaratkan KIND (suara mesin, bogie, interior, pantograf,
  spesifikasi mesin) tidak menawarkan kandidat; klakson dan suara roda-rel memakai status
  "Jenis belum dapat dipastikan" sesuai `RoleCatalog`.
- **Diagnosis** hanya menyimpulkan "belum terpasang" dari status Trainz; aset yang tidak ada di indeks
  hanya dicurigai bila Trainz memberi flag M. Pesan validator yang belum dipetakan ditampilkan apa adanya.
  Satu aturan perbaikan (R001, tambah entri kuid-table), berstatus terpandu.
- **Graf dependency turunan** memakai config yang sudah terbaca di sesi; `list-dependencies` belum punya
  parser karena bentuk barisnya belum pernah tertangkap (T10).
- IBM Plex Sans (SIL OFL 1.1) **tidak dibundel**; aplikasi memakainya bila terpasang, bila tidak
  Segoe UI. Bila nanti dibundel, sertakan teks lisensi OFL.

**Tampilan WPF belum pernah dijalankan atau dilihat.** Yang terverifikasi hanya: XAML terkompilasi,
seluruh solusi ter-build, dan perilaku ViewModel lolos uji.

## Kriteria penerimaan dengan bukti otomatis (simulator)

| Kriteria | Uji |
| --- | --- |
| AC-01, 02, 03, 05, 06, 26, 28 | `ConfigTests`, `DependencyTests` (R001) |
| AC-04, LIB-01 | `Bogie_candidates_AC04`, `Wrong_kind_is_rejected_with_reason_AC04`, `Reading_candidate_kind_makes_bogie_selectable_LIB01` |
| AC-08 | `Faulty_dependency_is_not_reported_as_missing_AC08`, `Repair_cards_follow_problem_classes_and_offer_actions_AC08` |
| AC-09 … AC-14, AC-22 | `TransactionTests`, `Asset_open_elsewhere_is_not_taken_over_AC09`, `External_change_after_draft_stops_with_conflict_AC10`, `Uncertain_submit_is_reconciled_from_history_AC11`, `History_lists_work_and_undo_restores_exact_bytes_AC13`, `Undo_after_external_edit_shows_conflict_AC14` |
| AC-15 | `Failure_on_fifth_asset_keeps_four_successes_AC15` |
| AC-16, 17, 18, 19, 25 | `TextureTests`, `ChannelPackerTests`, `Texture_panel_blocks_on_name_collision_until_resolved_AC18`, `Texture_folder_mode_works_without_trainz_AC25` |
| AC-21 | `Same_kuid_in_two_profiles_stays_separate_CONNECT01_AC21` |
| AC-24 | `Partial_repair_is_reported_as_partial_not_healthy_AC24` |
| AC-29 | `Sound_workshop_previews_and_replaces_wav_without_new_kuid_AC29` |
| AC-30 | `Initial_product_outside_allowed_list_is_a_conflict_AC30` |
| AC-33, 35, 37 | `Draft_keeps_changes_from_different_modules_AC33`, `Library_search_by_name_kuid_and_status_filter_AC35`, `Draft_survives_navigation_AC37` |
| AC-34 | `Texture_folder_mode_works_without_trainz_AC25` (peta modul) |
| EDIT-AC | `Editor_form_saves_plan_and_shows_diff_EDITAC`, `Text_editor_saves_valid_text_and_blocks_invalid_EDITAC` |

Belum ada bukti otomatis: AC-07 (rantai bertingkat hanya diuji di `DependencyGraph`, belum dari Trainz),
AC-20 (pemilik/cakupan tracksound bersama), AC-23 (pembedaan validasi vs uji visual hanya lewat label
hasil), AC-27, AC-31, AC-32, AC-38/39 (desain dan tampilan; lihat T2).

## Status uji (lingkungan cloud, Linux, .NET SDK 10.0.113)

`dotnet test RailNexus.slnx`: 211 dari 212 lolos. Yang gagal,
`TrainzUtilAdapterTests.Edit_folder_from_real_output_shape_must_exist`, adalah uji Tahap 0 yang
memerlukan path Windows (`C:/…`, sesuai bukti `edit-already-open.json`); di Linux folder sementara
berbentuk `/tmp/…`. Di Windows uji ini diharapkan lolos. Seluruh solusi, termasuk `RailNexus.App`
(WPF), ter-build tanpa peringatan, dan `dotnet publish -p:PublishProfile=win-x64` menghasilkan paket
mandiri; paket itu belum dijalankan.

**Belum diverifikasi sama sekali:** tampilan dan perilaku WPF (belum pernah dijalankan), pemutar
suara `MediaPlayer`, dialog berkas, dan seluruh perilaku terhadap TRS19 asli selain yang sudah tercatat di
`tahap0.md`. Perintah `searchbycategory` dan `printconfig` ada di profil dengan `verified: false`, jadi
pengindeksan dari aplikasi dan pembacaan KIND kandidat belum berfungsi pada Trainz asli sampai T4.

## TODO lokal (Windows + TRS19)

Dipindah dan diperinci di [`handoff-windows.md`](handoff-windows.md) (T1–T14).
