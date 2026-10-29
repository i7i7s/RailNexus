# Tahap 3 — matriks format (spike, belum diimplementasikan)

PRD bagian 28: Tahap 3 bersyarat, setiap format punya gerbang sendiri, dan pekerjaan format biner tidak
diberi estimasi sebelum sampel dan spesifikasinya diperiksa. Rilis inti berjalan tanpa fitur ini.

Di lingkungan pengembangan cloud tidak ada Trainz, tidak ada sampel berkas biner Trainz, dan tidak ada
spesifikasi resmi yang dapat diverifikasi. Karena itu **tidak ada pembaca/penulis format Tahap 3 yang
ditulis**: menulisnya dari dugaan akan melanggar PRD 23.2 dan AC-19. Yang sudah ada hanya perilaku aman:
isi yang tidak dikenali dilaporkan "tidak dikenali" tanpa mengubah sumber
(`Unknown_texture_format_is_not_faked_and_source_is_untouched_AC19`).

| Kemampuan | Modul (PRD) | Status sekarang | Yang dibutuhkan sebelum mulai | Gerbang |
| --- | --- | --- | --- | --- |
| Baca `.texture` terkompilasi | Tekstur Alur C (13.5), RN-015 | Tidak dikenali → dilaporkan, sumber utuh | Sampel `.texture` per varian dari aset milik sendiri + gambar sumbernya; dokumentasi varian; izin/lisensi | Matriks varian + golden output per varian; hasil tidak diklaim identik dengan sumber asli |
| Tulis `.texture` | — | Tidak direncanakan | Hanya bila Trainz tidak lagi membangunnya sendiri dari `.texture.txt` | — |
| Preview model (`.im`) | Lihat Model (15), RN-016 | Belum ada; kartu "Lihat model" nonaktif dengan alasan | Sampel `.im` dari aset milik sendiri (beberapa versi format), dokumentasi atau pustaka berlisensi; pemilihan renderer WPF/D3D | VIEW-AC pada format terbatas; LOD dan material yang tidak didukung diberi label |
| Animasi (`.kin`) | Gerak & Animasi (18) | Belum ada | Sampel animasi + dokumentasi; P1 diagnosis lebih dulu | Diagnosis tanpa authoring |
| Konversi mesh legacy (`.pm` → `.im`) | Workshop Mesh (16.1), RN-017 | Belum ada | Pembaca `.pm` dan penulis `.im` yang dapat diuji round-trip | Struktur, visual, round-trip tervalidasi |
| Geometri bayangan | Workshop Mesh (16.2) | Belum ada | Profil legacy yang membutuhkannya; penulis mesh | Hanya untuk profil yang sesuai |
| Titik pemasangan | Attachment (17) | Belum ada | Jalur ekspor mesh yang teruji | Titik tersimpan dan terhubung, diuji di Trainz |

## Langkah riset yang dapat dijalankan di mesin Windows

1. Kumpulkan sampel hanya dari aset **buatan sendiri** (mis. aset uji Tahap 0) di folder di luar repo.
   Jangan menyimpan berkas milik pembuat mod lain ke repo.
2. Untuk setiap sampel catat: build Trainz, ukuran, 16–64 byte pertama (heksadesimal), dan gambar/mesh
   sumber yang menghasilkannya.
3. Tulis temuan di dokumen ini. Baru setelah ada pola yang konsisten pada beberapa sampel, buat spike
   pembaca baca-saja dengan uji golden output.
