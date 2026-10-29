# Tugas untuk AI agent lokal (Windows + TRS19)

> Berikan berkas ini ke AI agent yang berjalan di komputer Windows pengguna (Claude Code, Codex, atau
> sejenisnya), misalnya dengan perintah: *"Baca dan kerjakan TUGAS-AGENT-LOKAL.md."*
> Rincian teknis setiap tugas ada di [`docs/handoff-windows.md`](docs/handoff-windows.md).

## Peranmu

Kamu adalah agent pengembang yang bekerja di komputer Windows milik pengguna, tempat **Trainz Railroad
Simulator 2019 (TRS19, build 117026)** dan `TrainzUtil.exe` terpasang. Proyek ini, **RailNexus**, adalah
aplikasi desktop C#/.NET 10 + WPF untuk mengedit mod Trainz secara aman (draft → transaksi dengan
backup → submit → validasi → undo).

Seluruh kode sampai sekarang dibuat di lingkungan cloud **tanpa Trainz dan tanpa layar**. Logikanya
lolos 211 dari 212 uji otomatis dengan simulator, tetapi:

- aplikasi WPF **belum pernah dijalankan atau dilihat**;
- perilaku terhadap TRS19 asli baru terbukti untuk siklus dasar Tahap 0 (`docs/tahap0.md`);
- dua perintah TrainzUtil baru (`searchbycategory`, `printconfig`) belum diverifikasi.

Tugasmu: **menjalankan, menguji, memperbaiki, dan mencatat bukti** di mesin nyata. Bahasa komunikasi
dengan pengguna, teks UI, komentar kode, dan pesan commit: **Bahasa Indonesia**, mengikuti gaya kode yang
sudah ada.

## Langkah 0 — siapkan kode

```powershell
# Bila repo belum ada di komputer:
git clone https://github.com/i7i7s/RailNexus.git
cd RailNexus

# Bila sudah ada:
git fetch origin
git checkout claude/loving-tesla-dez5q9
git pull origin claude/loving-tesla-dez5q9

# Bekerja di branch sendiri, jangan langsung di main:
git checkout -b lokal/uji-windows
```

Pastikan `.NET 10 SDK` terpasang (`dotnet --list-sdks`). Bila belum, minta pengguna memasangnya dari
https://dotnet.microsoft.com/download/dotnet/10.0 — jangan memasang sendiri tanpa izin.

Baca sebelum mengubah apa pun: `README.md`, `docs/tahap0.md`, `docs/tahap1.md`,
`docs/handoff-windows.md`, `docs/panduan.md`, lalu PRD (`PRD-RailNexus-v1.2.md`) bagian 21, 23, dan 27.

## Aturan yang tidak boleh dilanggar

1. **Hanya aset uji yang boleh ditulis.** Aset uji yang ada: `<kuid:-2:117104311>`. Aset uji baru harus
   dibuat lewat `make-test-asset` dan memuat penanda `RailNexus-uji-Tahap0`. Jangan pernah membuka
   untuk diedit, commit, revert, atau mengimpor aset lain.
2. **Jangan mengarang keluaran TrainzUtil.** Setiap bentuk keluaran baru harus ditangkap dengan
   `capture` ke `fixtures/trainzutil/trs19/`, dan isi fixture tidak boleh disunting.
3. **`verified: true` di `src/RailNexus.Trainz/profiles/trs19.json` hanya dengan bukti**: fixture nyata +
   uji parser yang lolos + nama fixture di kolom `evidence`.
4. **Jangan menyimpan isi config, tekstur, atau suara milik pembuat mod lain ke repo.** Untuk aset orang
   lain, catat ringkasan atau diagnosis saja (seperti keluaran `parse-check`).
5. **Jangan menaikkan peran ke `SupportLevel.AutomatedTested`** sebelum skenario T8 lolos untuk peran itu.
6. **Jangan melewati, menonaktifkan, atau melemahkan uji** agar hijau. Perbaiki penyebabnya.
7. **Jangan push ke `main`** dan jangan force-push. Push hanya ke `lokal/uji-windows`.
8. Ingat temuan Tahap 0: exit code TrainzUtil selalu 0 (hasil dibaca dari baris
   `OK (n Errors, m Warnings)`); `installfrompath` menolak path berspasi; Trainz harus berjalan dengan
   **Advanced Debug Tools** aktif **dan sudah dibuka ulang** setelah pengaturan itu diubah.

## Kapan berhenti dan bertanya ke pengguna

- Trainz perlu dibuka, ditutup, atau dibuka ulang, atau pengaturan Launcher perlu diubah.
- Kamu perlu melihat atau menilai sesuatu di layar (tampilan aplikasi, tekstur di Trainz, suara) dan
  tidak punya kemampuan tangkapan layar/mendengar: minta pengguna mengamati dan melaporkan.
- Ada tindakan yang menyentuh aset selain aset uji, atau berisiko pada data Trainz pengguna.
- Uji gagal dan perbaikannya mengubah perilaku keamanan transaksi (`src/RailNexus.Core/Tx/`).
- Memasang perangkat lunak apa pun.

## Daftar tugas (kerjakan berurutan)

Rincian perintah, hasil yang diharapkan, dan file yang terlibat ada di `docs/handoff-windows.md` dengan
nomor yang sama.

| No | Tugas | Butuh pengguna? | Selesai bila |
| --- | --- | --- | --- |
| T1 | `dotnet test RailNexus.slnx` di Windows | Tidak | Semua uji lolos (212), termasuk uji path Windows |
| T2 | Jalankan aplikasi (`dotnet run --project src/RailNexus.App`), bandingkan dengan kanvas desain di 1440×900, 1280×720, skala teks 125–150 %; perbaiki XAML | Ya, untuk melihat layar | Semua halaman terbuka tanpa error, tidak ada kontrol terpotong, status selalu berteks |
| T3 | Sambungkan + daftarkan aset uji di Pengaturan | Ya (Trainz berjalan) | "Build 117026 · dapat mengedit aset uji" |
| T4 | Tangkap & verifikasi `searchbycategory` dan `printconfig` | Ya (Trainz berjalan) | Fixture baru, uji parser lolos, profil diperbarui dengan bukti |
| T5 | Perbarui indeks dari aplikasi | Ya | Pustaka terisi, cari nama/KUID berjalan |
| T6 | Siklus aset uji lewat aplikasi (editor → Terapkan → Undo, juga editor teks) | Ya | Berhasil, Undo mengembalikan byte identik |
| T7 | Workshop Tekstur di Trainz asli (`texture` CLI + aplikasi) | Ya, untuk melihat tekstur | Orientasi, warna, alpha benar di Trainz |
| T8 | Aset uji kendaraan + uji setiap peran (klakson, suara mesin, bogie, suara roda-rel, P1) | Ya | Hasil per peran tercatat; peran yang lolos boleh dinaikkan |
| T9 | Skenario kegagalan nyata AC-09, 10, 14, 22 | Ya | Hasil cocok dengan pesan transaksi, tercatat di `docs/tahap0.md` |
| T10 | Tangkap `list-dependencies` aset berdependency, buat parser | Ya | Parser + uji; graf turunan memakai sumber nyata |
| T11 | `parse-check` TV/VE: analisis tracksound 0/60, `CFG-UNEXPECTED`, tag queues | Ya | Penyebab dicatat; perbaikan parser dengan fixture sintetis |
| T12 | Suara: preview, Stop, ganti WAV pada aset uji | Ya, untuk mendengar | AUDIO-AC dan AC-29 terbukti |
| T13 | `dotnet publish src/RailNexus.App/RailNexus.App.csproj -p:PublishProfile=win-x64`, uji di mesin tanpa SDK | Ya | `RailNexus.exe` berjalan, folder `licenses/` disertakan |
| T14 | (Opsional) Kumpulkan sampel format untuk Tahap 3 | Ya | Temuan di `docs/tahap3-spike.md`, sampel di luar repo |

Singkatan CLI: `rn` = `dotnet run --project src/RailNexus.Cli --`. Bila TrainzUtil tidak di lokasi Steam
standar, tambahkan `--trainz "<folder TRS19>"`.

## Cara bekerja per tugas

1. Umumkan ke pengguna tugas yang akan dikerjakan dan apa yang perlu mereka lakukan.
2. Jalankan langkahnya; simpan bukti (id transaksi dari `rn list`, nama fixture, ringkasan keluaran).
3. Bila ada perbaikan kode: ubah seminimal mungkin, tambah/ubah uji, jalankan `dotnet test RailNexus.slnx`.
4. Perbarui dokumen: temuan TrainzUtil di `docs/tahap0.md` (tabel *Temuan*), status fitur di
   `docs/tahap1.md`, centang tugas di `docs/handoff-windows.md`.
5. Commit dengan pesan Bahasa Indonesia yang menyebut bukti, lalu `git push -u origin lokal/uji-windows`.

## Format laporan ke pengguna setelah setiap tugas

```text
T<n> — <judul>: LOLOS | GAGAL | SEBAGIAN
Bukti: <id transaksi / fixture / pengamatan pengguna>
Diubah: <file yang diubah, atau "tidak ada">
Belum pasti: <hal yang masih perlu dicek>
Berikutnya: <tugas berikut dan apa yang dibutuhkan dari pengguna>
```

## Peta kode singkat

| Bagian | Lokasi |
| --- | --- |
| Adapter TrainzUtil, profil, parser keluaran | `src/RailNexus.Trainz/` |
| Transaksi, jurnal, undo, rekonsiliasi | `src/RailNexus.Core/Tx/` |
| Parser/editor config, diff | `src/RailNexus.Core/Config/` |
| Peran, mesin modifikasi, draft | `src/RailNexus.Core/Roles/` |
| Dependency, diagnosis, aturan perbaikan | `src/RailNexus.Core/Dependencies/` |
| Tekstur, suara, batch, muatan | `src/RailNexus.Core/Texture/`, `Audio/`, `Batch/`, `Cargo/` |
| ViewModel semua layar (logika UI, teruji) | `src/RailNexus.App.Core/ViewModels/` |
| XAML, tema, pemutar suara WPF | `src/RailNexus.App/` |
| CLI uji integrasi | `src/RailNexus.Cli/Program.cs` |
| Uji + simulator Trainz | `tests/RailNexus.Tests/` |

Logika layar ada di `RailNexus.App.Core` dan sudah diuji; perbaiki tampilan di XAML, jangan memindahkan
logika ke code-behind.
