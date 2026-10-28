# Tahap 0 — membuktikan integrasi

Rujukan: PRD bagian 21, 23, 27, 28 (Tahap 0). Desain UI (RN-000) ada di kanvas
https://claude.ai/artifact/D44DxCuuND51aHU3REbVuP.

**Gerbang Tahap 0:** siklus baca → ubah → submit → periksa → pulihkan dapat diulang pada
aset uji yang dapat dibuang, termasuk kasus timeout dan crash.

## Isi repositori

| Bagian | Isi |
| --- | --- |
| `src/RailNexus.Core` | `Kuid`, hasil operasi tiga nilai (`OpResult`), kontrak `ITrainzAdapter`, `ProcessRunner`, mesin transaksi (`Tx/`), patch config minimal |
| `src/RailNexus.Trainz` | Adapter TrainzUtil berbasis profil build, klasifikasi keluaran, corpus, pembuat aset uji |
| `src/RailNexus.Trainz/profiles/trs19.json` | Pemetaan operasi → perintah TrainzUtil untuk TRS19 |
| `src/RailNexus.Cli` | Alat uji `railnexus` |
| `tests/RailNexus.Tests` | Test unit + simulator kegagalan |
| `fixtures/trainzutil/trs19` | Corpus keluaran nyata TrainzUtil |

## Aturan keamanan yang ditegakkan kode

- Operasi tulis (`edit`, `commit`, `revert`, impor) **tidak dijalankan** bila entri profilnya
  `verified: false`. Semua entri awal belum terverifikasi.
- Operasi tulis hanya untuk KUID aset uji yang diberikan lewat `--kuid`. KUID lain ditolak
  sebelum proses dijalankan.
- `cycle` berhenti bila config aset tidak memuat penanda `RailNexus-uji-Tahap0`.
- `import` hanya menerima folder buatan `make-test-asset`.
- Keluaran yang belum terverifikasi atau tidak dikenali menjadi `Unknown`, tidak pernah `Success`.
- Path pribadi di corpus disamarkan menjadi `%USERPROFILE%` / `%USERNAME%`.

## Transaksi (PRD 21.2)

`Started → Prechecked → Locked → EditOpened → BaseVerified → Snapshotted → Writing →
Written → Submitting → Submitted | SubmitUncertain → Verified → Closed`, dengan
`RollingBack → RolledBack` saat gagal. Jurnal ditulis dengan *write-through* ke
`%LOCALAPPDATA%\RailNexus\tahap0\tx\<id>\`.

| Kriteria | Bukti otomatis (simulator) |
| --- | --- |
| AC-09 aset sudah open for edit | `Asset_already_open_for_edit_is_not_taken_over_AC09` |
| AC-10 berkas berubah setelah rencana | `File_changed_after_plan_stops_with_conflict_AC10` |
| AC-11 commit timeout | `Commit_timeout_is_uncertain_and_not_retried_AC11` |
| AC-12 crash setelah tulis | `Crash_after_write_before_submit_is_found_and_recovered_AC12` |
| AC-13 undo setelah commit | `Apply_then_undo_restores_exact_bytes_AC13` |
| AC-14 undo setelah edit eksternal | `Undo_after_external_edit_shows_conflict_and_keeps_external_change_AC14` |
| AC-22 ruang disk habis | `Not_enough_disk_for_snapshot_writes_nothing_AC22` |
| AC-05/06 config | `ConfigPatchTests` |

Simulator membuktikan logika. Gerbang Tahap 0 baru lolos setelah siklus yang sama berjalan
di TRS19 sungguhan.

## Langkah pada mesin uji

```bash
dotnet test RailNexus.slnx
```

1. Aktifkan **Advanced Debug Tools** di Launcher → Trainz Settings, lalu jalankan TRS19.
2. Tangkap daftar perintah dan versi:
   ```bash
   dotnet run --project src/RailNexus.Cli -- probe
   ```
3. Perbarui `profiles/trs19.json` dari keluaran nyata (sintaks, timeout) dan tandai
   `verified: true` hanya untuk perintah yang terbukti. Tangkap contoh tambahan dengan
   `capture <nama> -- <argumen>`.
4. Buat dan impor aset uji (pakai user ID Trainz Anda dan nomor konten yang tidak terpakai):
   ```bash
   dotnet run --project src/RailNexus.Cli -- make-test-asset --kuid "<kuid:USERID:990001>"
   ```
5. Jalankan siklus:
   ```bash
   dotnet run --project src/RailNexus.Cli -- cycle --kuid "<kuid:USERID:990001>"
   ```
6. Uji kegagalan nyata: tutup Trainz saat `commit` berjalan, lalu `list` dan `reconcile`.

## Status gerbang Tahap 0 — LOLOS (2026-10-28, TRS19 build 117026)

Semua uji pada aset uji `<kuid:-2:117104311>`; aset lain tidak disentuh.

| Skenario | Hasil nyata | Transaksi |
| --- | --- | --- |
| Perbaiki aset uji faulty lewat transaksi | `Succeeded` — "1 masalah lama selesai", flag `f` | `20261028T063652-d5a0e7e8` |
| Baca → ubah → submit → periksa | `Succeeded`; deskripsi baru, semua baris lain identik | `20261028T063712-4ceaaed6` |
| Undo setelah commit (AC-13) | `Succeeded`; config kembali **identik byte** | `20261028T063727-f9d8c182` |
| Crash setelah tulis, sebelum submit (AC-12) | Trainz: aset `E` terbuka; `list` menemukan tx di fase `Written`; `reconcile` → `RolledBack`, status kembali `e` | `20261028T064030-aa91af91` |
| Commit timeout 100 ms (AC-11) | `Uncertain`, submit **tidak diulang**; Trainz: aset tetap `E` (submit tidak sampai). `reconcile` → `NeedsAttention`; dengan pilihan eksplisit `--discard-pending-edit` → `RolledBack`, hash isi = keadaan awal | `20261028T064345-81d7d183` |
| Commit timeout 300 ms | Commit selesai sebelum batas; transaksi normal `Succeeded` (lalu di-undo) | `20261028T064200-37347151` |

Belum diuji pada Trainz asli (sudah diuji di simulator): konflik berkas berubah setelah rencana (AC-10),
aset sudah dibuka pihak lain (AC-09), undo setelah edit eksternal (AC-14), ruang disk habis (AC-22).

## Temuan

| Tanggal | Temuan | Bukti |
| --- | --- | --- |
| 2026-10-28 | TrainzUtil TRS19 (Steam) menolak semua perintah bila Trainz tidak berjalan: stdout `- Trainz is not running. Please start Trainz and try again.`, exit 1, ±95 ms | `fixtures/trainzutil/trs19/probe-help.json` |
| 2026-10-28 | Wiki TrainzUtil N3V tidak dapat diakses (HTTP 500); sintaks profil belum terverifikasi | `profiles/trs19.json` → `notes` |
| 2026-10-28 | Setelah *Advanced Debug Tools* aktif dan Trainz **ditutup total lalu dibuka ulang**, TRS19 mendengar di port TCP lokal dan TrainzUtil tersambung. Build: **117026**. Mengubah pengaturan tanpa restart proses tidak cukup. | `probe-help.json`, `probe-version.json` |
| 2026-10-28 | **Exit code TrainzUtil selalu 0**, termasuk saat error. Hasil hanya dapat dibaca dari baris penutup `OK (n Errors, m Warnings)` dan baris berawalan `-` (error) / `!` (peringatan) / `+` (hasil) / `;` (log). | `unknown-command.json`, `status-invalid-kuid.json` |
| 2026-10-28 | `status` mengembalikan `<KUID> : <flags> : <kategori> : <nama>`; flag huruf besar = benar (format resmi di `help`). `Unknown KUID` = aset tidak ada. | `status-after-install.json`, `status-after-commit.json` |
| 2026-10-28 | `installfrompath` **menolak path berspasi** walau dikutip ("Invalid input file path"); berhasil dari path tanpa spasi. Aset hasil impor dibiarkan **terbuka untuk diedit** ("Skipped commit") sehingga perlu `commit`. Adapter memakai nama pendek 8.3. | `install-test-asset*.json` |
| 2026-10-28 | `edit` mengembalikan folder kerja di `%LOCALAPPDATA%\N3V Games\trs19\build …\editing\kuid … <nama>\` dengan garis miring `/`. | `edit-already-open.json` |
| 2026-10-28 | Trainz **menormalkan config** saat memasang: menambah BOM, baris kosong di awal, mengubah baris komentar `//` menjadi tag `//`, dan menambah `kuid-table {}`. Perubahan ini milik Trainz, bukan RailNexus, dan harus dibedakan saat membandingkan hasil. | `printconfig-after-install.json`, berkas `editing\…\config.txt` |
| 2026-10-28 | `validate` menolak aset yang masih terbuka ("is not available"); setelah commit, aset uji versi pertama **faulty** dengan VE45/60/62/26/28. Aset uji diperbaiki: tag `texture`, container `thumbnails`, tanpa tag/komentar asing. | `validate-after-install.json`, `validate-after-commit.json` |
| 2026-10-28 | `generateKUID` memberi `<kuid:-2:…>` (rentang lokal; pengguna belum memiliki author ID terdaftar). | `generatekuid.json` |
| 2026-10-28 | Keluaran TrainzUtil adalah UTF-8; dibaca dengan codepage konsol menghasilkan `�`. | `printconfig-after-install.json` |
| 2026-10-28 | `status <KUID> [..]` **hanya memproses KUID pertama** walau `help` menyebut banyak KUID (90 KUID → 1 baris hasil, tanpa error). Indeks pustaka membaca status per KUID. | `status-batch-VE.json` |
| 2026-10-28 | `filterbystatus I <KUID> [..]` mengembalikan baris berformat `status` untuk **setiap** KUID terpasang dalam satu panggilan. Indeks 213 aset: ±1 dtk (vs ±23 dtk per KUID, ±110 ms/aset). `print-assets` dan `list` hanya mengulang KUID. | `filterbystatus-I-3.json`, `print-assets-3.json`, `list-3.json` |
| 2026-10-28 | `searchbycategory <kat>` mengembalikan baris `+ <kuid> : + #n : <kuid>` (diindentasi), didahului jumlah hasil. VE: 90, TV: 123 aset lokal. | `searchbycategory-VE.json`, `searchbycategory-TV.json` |
| 2026-10-28 | Parser vs config asli (`printconfig`, isi tidak disimpan): string multi-baris menyebabkan CFG-STRING/NOVALUE/AMBIGUOUS beruntun; setelah diperbaiki VE 60/60 tanpa error, TV 60/60 ter-parse (1 error `CFG-UNEXPECTED` pada baris berisi KUID saja — belum dianalisis). Peran: bogie 59/60, hornsound 9/60, enginesound 9/60, tracksound 0/60. Nilai kosong ditulis printconfig sebagai tag tanpa nilai. | keluaran `parse-check` |
| 2026-10-28 | Aset terpasang disimpan sebagai arsip `.tzarc` di `local\hash-xx\`; config hanya dapat dibaca lewat TrainzUtil saat Trainz berjalan. | struktur folder data |
| 2026-10-28 | Launcher saja maupun game di menu utama tetap menghasilkan "not running". TrainzUtil menyambung ke Trainz lewat 127.0.0.1 dan mensyaratkan **Advanced Debug Tools** aktif di Launcher Settings (teks di `TrainzUtil.exe`: "Please ensure that Trainz is running and that 'Advanced Debug Tools' are enabled in the Launcher Settings."). Saat diperiksa, proses TRS19 tidak membuka port TCP. Pesan kedua yang mungkin muncul: "- Trainz did not respond to connection attempt." | `probe-help.json`; string di `TrainzUtil.exe` |
