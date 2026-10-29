# Panduan singkat RailNexus

Panduan ini menjelaskan alur utama. RailNexus masih dalam pengembangan: **perubahan hanya dapat
ditulis ke aset uji** yang Anda daftarkan di Pengaturan, sampai setiap fitur lolos uji di Trainz asli.
Jangan memakai aset penting untuk mencoba.

## 1. Sambungkan ke Trainz

1. Di Trainz Launcher, buka **Trainz Settings** dan aktifkan **Advanced Debug Tools**.
2. Tutup Trainz sepenuhnya, lalu buka lagi. Pengaturan baru berlaku setelah Trainz dibuka ulang.
3. Buka RailNexus. Halaman **Sambungkan** menampilkan instalasi yang ditemukan. Bila tidak ada, pilih
   **Pilih folder instalasi lain…** dan tunjuk folder yang berisi `TrainzUtil.exe`.
4. Tekan **Periksa koneksi**, lalu **Hubungkan**.
   - *Terhubung · dapat mengedit aset uji*: build ini sudah diuji.
   - *Terhubung · hanya baca*: build belum diuji; Anda dapat melihat dan memeriksa, tetapi tidak menulis.
   - *Trainz belum tersambung*: ikuti langkah yang ditampilkan.
5. Buka **Pengaturan** → **Aset uji yang boleh ditulis**, tambahkan KUID aset uji, **Simpan pengaturan**,
   lalu sambungkan ulang.

## 2. Cari mod dan pilih yang ingin diubah

1. Buka **Pustaka**. Bila masih kosong, isi kategori (mis. `TV, VE`) lalu **Perbarui indeks**. Bila
   muncul pesan "belum terverifikasi", isi indeks lewat CLI: `railnexus index --category TV,VE`.
2. Ketik nama mod atau tempel KUID (mis. `<kuid:123:456>`), atau pakai filter status.
3. Pilih aset lalu tekan **Mau ubah apa?** (atau Enter / klik dua kali).

## 3. Ubah komponen (klakson, suara mesin, suara roda-rel, bogie, dan lainnya)

1. Di **Mau edit apa?**, pilih tugas, misalnya **Ganti klakson**. Tugas yang tidak berlaku tetap
   tampil dengan alasannya.
2. Pilih lokasi (slot atau aturan), cari pengganti di pustaka lokal, lalu pilih kandidat.
   - Kandidat yang bermasalah, belum tersedia, atau jenisnya berbeda ditampilkan dengan alasan dan
     tidak dapat dipilih. Bila jenisnya belum terbaca, tekan **Baca jenis kandidat**.
   - Untuk klakson dan suara mesin, **Dengarkan yang sekarang** / **Dengarkan pengganti** memutar sampel
     bila berkas suaranya dapat dibaca. Ini bukan simulasi suara di Trainz.
3. Tekan **Gunakan pengganti ini**. Perubahan masuk **Rencana perubahan** di kanan; aset belum ditulis.
4. Anda boleh menambah perubahan lain dari modul lain (editor, tekstur, suara). Semuanya masuk satu draft.

## 4. Tekstur

- Dari aset: **Mau edit apa?** → **Tekstur & material**. Tanpa Trainz: **Workshop** → **Buka Workshop Tekstur**.
- Tambahkan gambar (PNG/TGA; format dikenali dari isi berkas). Tentukan tujuan gambar (warna, normal,
  parameter), perlakuan alpha, dan nama output.
- Bila nama bentrok, pilih: ganti nama, lewati, atau ganti berkas lama (dengan backup).
- **Konversi ke draft** (aset) atau **Simpan ke folder** (tanpa Trainz). Setiap hasil dibaca ulang dan
  dibandingkan per piksel; sumber tidak pernah diubah.
- Gambar di aset yang belum punya `.texture.txt` dapat dibuatkan metadatanya dari bagian
  **Metadata gambar di aset**.
- **Perakit PBR…** menyusun kanal R/G/B/A dari gambar atau nilai konstan pilihan Anda.

## 5. Terapkan & Selesai

1. Periksa **Rencana perubahan**: daftar perubahan, yang diurus otomatis, dan yang belum dapat dipastikan.
   **Editor lanjutan** → tab **Perubahan** menampilkan perbandingan baris config.
2. Tekan **Terapkan & Selesai**. RailNexus membuka aset lewat Trainz, membuat backup, menulis, submit,
   lalu memeriksa hasilnya.
3. Hasil selalu berlabel: *Berhasil*, *Berhasil dengan catatan*, *Konflik*, *Dibatalkan dan dipulihkan*,
   *Hasil submit belum pasti*, atau *Perlu tindakan*. Lolos validasi belum berarti tampilan dan suara
   benar; uji juga di Trainz.

## 6. Periksa dan perbaiki

- Dari aset: **Mau edit apa?** → **Periksa & perbaiki**. Dari navigasi: **Perbaikan** menampilkan aset
  yang menurut indeks bermasalah.
- **Periksa ulang** membaca ulang isi aset dan menjalankan validator Trainz.
- Setiap kartu menjelaskan penyebab, bukti, dan dampak, serta tindakan yang tersedia (pilih pengganti,
  buka target, tambahkan ke kuid-table, buka editor). Tidak semua masalah dapat diperbaiki otomatis.

## 7. Batalkan perubahan (Undo) dan pemulihan

- **Riwayat** mencatat setiap pekerjaan: waktu, aset, berkas, hasil, dan ukuran backup.
- **Batalkan perubahan terpilih** menjalankan Undo sebagai pekerjaan baru. Bila berkas sudah diubah di luar
  RailNexus, Undo berhenti dengan *Konflik* dan tidak menimpa perubahan itu.
- Pekerjaan yang terputus (Trainz/aplikasi tertutup, timeout) tampil sebagai *Belum selesai*. Pilih
  **Periksa dan pulihkan**; bila aset masih terbuka setelah submit yang belum pasti, Anda dapat memilih
  **Batalkan edit tertunda** secara sadar.

## 8. Batch

**Workshop** → **Batch & preset**: pilih beberapa aset, tentukan peran dan aset lama → baru, **Tinjau
rencana**, lalu **Jalankan pekerjaan**. Setiap aset diproses dengan transaksinya sendiri; bila satu aset
gagal, keberhasilan aset lain tetap tercatat dan dapat dibatalkan dari Riwayat.
