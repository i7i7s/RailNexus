# RailNexus

## Product Requirements Document dan Spesifikasi Alur Kerja

**Versi dokumen:** 1.2 · **Tanggal:** 13 Oktober 2026  
**Status:** rancangan produk untuk pengembangan; kemampuan yang disebut sebagai target belum berarti sudah tersedia atau teruji.  
**Platform:** aplikasi desktop Windows x64.  
**Sasaran integrasi:** TRS19 dan versi lebih baru melalui profil kompatibilitas yang diuji.  
**Nama proyek:** RailNexus — Pusat Pengelolaan Aset Trainz. Pemeriksaan merek untuk distribusi publik masih menjadi pekerjaan prarilis.

> Edit mod Trainz semudah memilih aset, memilih perubahan, lalu menekan Selesai. RailNexus mengurus langkah teknis di belakang layar.

Dokumen ini menggabungkan PRD, spesifikasi pengalaman pengguna, rancangan teknis awal, prioritas pengembangan, dan kriteria penerimaan. Seluruh alur RailNexus di bawah merupakan rancangan baru. Nama aplikasi lain dipakai untuk menjelaskan kebutuhan yang sudah dikenal pengguna. Kebutuhan dari AssetX, Soundz, Trainz Mesh Viewer 2, Train Swayer Utility, QuickShadows, Images2TGA, PM2IM, Attachment Maker, dan TextureTXT masuk ke satu rancangan aplikasi RailNexus dengan implementasi sendiri. Pemetaan lengkap dan tahap pengiriman setiap kemampuan terdapat di bagian 19.

**Perubahan versi 1.1:** nama proyek menjadi RailNexus; pusat produk menjadi pengelolaan aset dan dependency lintas jenis; alur utama, prioritas, ukuran keberhasilan, dan backlog diperbarui. Tracksound masuk cakupan inti untuk penggantian aturan yang sudah didukung. Penggantian hornsound, enginesound, tracksound, dan bogie merupakan spesialisasi mesin modifikasi yang sama.

**Perubahan versi 1.2:** pengalaman edit mod secara plug-and-play menjadi janji utama produk; setiap fitur memiliki UI sesuai tugasnya; ditambahkan spesifikasi layar dan instruksi agar AI menggambar mockup serta alur layar terlebih dahulu. Pekerjaan pertama AI penerima PRD adalah desain UI, sebelum implementasi aplikasi. Instruksi siap pakai terdapat pada bagian 33.

## 1. Masalah yang ingin diselesaikan

Pengguna Trainz sering mengetahui hasil yang diinginkan, tetapi kesulitan menjalankan langkah teknisnya: mengganti klakson, mengatur suara rel, memilih set suara mesin, mengganti komponen visual, menyiapkan tekstur, atau menyelesaikan dependency hilang. Pekerjaan sederhana dapat membutuhkan pencarian aset, penyalinan KUID, penyuntingan beberapa bagian config, pengolahan file, submit, dan pemeriksaan ulang.

Pekerjaan semakin berat ketika sebuah aset memiliki banyak referensi, puluhan addon perlu diperbarui, atau masalah sebenarnya berasal dari dependency yang lebih dalam. Aset dapat berupa kendaraan, rel/spline, scenery, bangunan, industri, komponen suara, mesh library, maupun jenis lain; daftar kemampuan mengikuti jenis dan struktur aset yang ditemukan. Pengguna juga berpindah antara editor teks, pengolah gambar, pemutar suara, viewer, dan Content Manager.

RailNexus mengubah pekerjaan tersebut menjadi alur berdasarkan tujuan pengguna. Kesulitan teknis tetap ditangani dengan benar, tetapi muncul di layar hanya ketika pengguna membutuhkannya.

### 1.1 Janji utama produk

- **Edit mod/KUID tanpa repot:** pengguna cukup memilih aset, memilih bagian yang ingin diubah, mengatur melalui UI, lalu menekan Selesai.
- Mengganti komponen tanpa mengetik atau menyalin KUID secara manual.
- Menjelaskan masalah aset dengan bahasa yang bisa ditindaklanjuti.
- Menyiapkan gambar, metadata tekstur, dan berkas pendukung dalam satu tempat.
- Menyimpan perubahan, melakukan submit lokal, dan memeriksa hasilnya melalui satu tindakan akhir.
- Menyediakan riwayat, backup, dan pemulihan yang dapat dipahami pengguna.

### 1.2 Arti “Selesai”

Pada aset terpasang, tombol **Terapkan & Selesai** menjalankan rencana perubahan yang sudah ditampilkan, kemudian memeriksa keadaan aset setelah submit. Hasil harus dibedakan menjadi berhasil, berhasil dengan peringatan, dipulihkan, atau perlu tindakan pengguna.

Pada folder proyek, tombol **Simpan hasil** hanya menyimpan berkas proyek. Aplikasi tidak boleh menampilkan “Sudah masuk Trainz” jika proses impor dan pemeriksaan belum dilakukan.

Istilah submit dalam produk ini berarti menyelesaikan perubahan lokal di Content Manager. Upload atau publikasi ke Download Station adalah tindakan berbeda dan di luar alur otomatis ini.

### 1.3 Definisi plug-and-play RailNexus

Plug-and-play adalah pengalaman pemakaian: setelah koneksi awal ke instalasi Trainz, pengguna dapat langsung mengerjakan perubahan yang didukung tanpa menghafal folder, tag config, KUID dependency, atau urutan perintah Content Manager.

**Alur utama: Pilih mod/KUID → Mau edit apa? → Pilih atau atur lewat UI → Selesai.**

Pengguna boleh mencari dengan nama atau menempelkan KUID bila sudah mengetahuinya. KUID merupakan pintu pencarian opsional, bukan persyaratan agar dapat mengedit. RailNexus menampilkan aset yang ditemukan dan bagian yang dapat diedit pada profil tersebut.

Pada pekerjaan yang didukung, aplikasi menangani penentuan lokasi referensi, penyusunan config, sinkronisasi dependency yang sesuai, backup, penulisan, submit lokal, serta pemeriksaan hasil. Ringkasan perubahan tampil langsung di halaman kerja sebelum tombol Selesai; pengguna tidak harus melewati halaman konfirmasi tambahan untuk perubahan sederhana.

Kemampuan otomatis tetap bergantung pada jenis aset, akses berkas, dan profil dukungan. Jika pekerjaan tidak dapat dilanjutkan, layar menjelaskan bagian yang bermasalah dan menyediakan langkah berikut yang spesifik. Mode manual tersedia melalui tombol **Buka editor lanjutan**, dengan aset dan lokasi yang relevan sudah dipilih.

### 1.4 Hubungan fitur utama dengan workshop

Pengalaman edit mudah adalah fitur utama. Pustaka, dependency, suara, tekstur, model, perbaikan, dan editor merupakan alat untuk menyelesaikan tujuan itu. Pengguna tidak perlu memilih nama utilitas, memahami mesin konversi, atau memulai pekerjaan dari dashboard teknis.

Contoh: memilih **Ganti klakson** langsung membuka UI pencarian dan preview suara; memilih **Edit tekstur** langsung membuka gambar serta pengaturannya; memilih **Perbaiki mod** langsung membuka diagnosis dan tindakan yang tersedia. Konteks aset tetap terbawa saat berpindah panel.

## 2. Pengguna dan kebutuhan

| Pengguna | Kebutuhan utama | Keputusan desain |
| --- | --- | --- |
| Pemain biasa | Kereta berfungsi dan mudah dimodifikasi | Nama komponen, gambar, status sederhana, pilihan yang relevan |
| Pengoleksi banyak addon | Mencari missing dependency dan mengubah banyak aset | Filter masalah, pekerjaan massal, hasil per aset |
| Pembuat/reskinner | Mengelola config, gambar, suara, material | Editor terstruktur, mode teks, workshop tekstur |
| Pembuat aset berpengalaman | Memeriksa detail dan mengulang proses | Diff, log, preset, aturan yang transparan |

**Skenario penggunaan yang setara:**

| Tujuan pengguna | Alur ringkas | Bukti hasil |
| --- | --- | --- |
| Mengganti klakson | Pilih kendaraan → Hornsound → dengarkan kandidat → pilih → Selesai | Referensi klakson benar dan hasil submit diperiksa |
| Mengganti suara rel | Pilih aset pemilik aturan → Tracksound → pilih aturan/kondisi → suara baru → Selesai | Aturan yang dipilih berubah; kondisi lain tetap terjaga |
| Mengganti set suara mesin | Pilih kendaraan → Enginesound → pilih set yang sesuai → Selesai | Jenis target benar dan set yang dipilih terpasang |
| Memperbaiki dependency | Pilih aset bermasalah → lihat rantai penyebab → unduh/impor/ganti yang sesuai | Pemeriksaan ulang membuktikan masalah selesai atau tersisa |
| Mengubah komponen visual | Pilih peran, misalnya interior, pantograph, bogie, atau mesh library → kandidat → tinjau | Lokasi referensi tepat, pemeriksaan visual dipisahkan |
| Menyiapkan tekstur | Masukkan gambar → pilih peran → preview kanal → ekspor/terapkan | Gambar dan metadata konsisten |
| Mengelola muatan | Pilih kendaraan/industri → queue → produk yang didukung → tinjau | Kapasitas, produk awal, daftar izin, dan tampilan muatan konsisten |

Tidak ada satu komponen yang menjadi gerbang bagi pekerjaan lain. Fitur yang siap dirilis ditentukan oleh profil dukungan dan prioritas, bukan oleh posisi contoh dalam tabel.

## 3. Prinsip produk

1. **Mulai dari tujuan.** Menu utama menggunakan “Ganti komponen”, “Atur suara”, “Perbaiki dependency”, dan “Siapkan tekstur”, bukan nama parser atau format internal.
2. **Pilihan otomatis harus bisa dijelaskan.** Pengguna dapat melihat mengapa suatu kandidat direkomendasikan atau ditolak.
3. **Detail muncul bertahap.** Tampilan sederhana menjadi default; tag dan KUID tetap tersedia di panel detail.
4. **Satu rencana, satu tindakan akhir.** Tidak ada permintaan konfirmasi berulang untuk setiap berkas dalam pekerjaan yang sudah dipilih.
5. **Tidak menyamakan belum diketahui dengan kompatibel.** Kesesuaian fungsi, perilaku suara, bentuk, dan validitas config ditampilkan sebagai pemeriksaan berbeda.
6. **Selalu sediakan pemulihan untuk perubahan terkelola.** Backup dibuat sebelum penulisan pertama.
7. **Alur penting bekerja tanpa AI dan tanpa akun RailNexus.** Aturan terstruktur menjadi dasar perubahan.
8. **Kualitas hasil lebih penting daripada banyaknya tombol.** Fitur yang belum diuji tidak ditampilkan seolah siap dipakai.
9. **Setiap tugas memiliki UI yang sesuai.** Pemilihan dependency menggunakan daftar kandidat; suara menggunakan pemutar; tekstur menggunakan preview gambar; model menggunakan viewport jika didukung. Editor teks adalah pilihan lanjutan.
10. **Desain visual mendahului implementasi.** AI terlebih dahulu menghasilkan mockup yang dapat dilihat, peta navigasi, dan contoh alur ujung ke ujung. Daftar fitur atau penjelasan teks saja belum memenuhi keluaran tahap desain.

## 4. Implementasi mandiri dan orisinalitas

Mengubah warna atau posisi tombol tidak otomatis menyelesaikan persoalan hak cipta. WIPO menjelaskan bahwa hak cipta melindungi ekspresi, termasuk program komputer, sementara ide dan metode sebagai konsep dibedakan dari ekspresinya. Ini merupakan dasar umum, bukan penetapan legal bahwa suatu produk tertentu bebas dari semua hak pihak lain. [Dasar hak cipta WIPO](https://www.wipo.int/en/web/copyright/faq-copyright)

Keputusan proyek:

- Menulis kode, desain antarmuka, ikon, teks bantuan, dan aturan otomasi sendiri.
- Menggunakan dokumentasi format yang tersedia dan contoh uji yang boleh digunakan.
- Tidak mengemas executable, kode, manual, ikon, atau aset PEVSoft ke produk tanpa izin/lisensi yang sesuai.
- Tidak menyalin susunan layar AssetX sebagai spesifikasi antarmuka. RailNexus memakai alur tugas terpandu dan antrean perubahan.
- Mencatat asal serta lisensi pustaka pihak ketiga yang benar-benar dipakai.
- Tidak menjadikan PEVSoft sebagai dependency wajib. Seluruh fitur inti harus merupakan implementasi RailNexus.
- Menggunakan Trainz yang terpasang sebagai pemilik proses impor dan validasi. Berkas program Trainz tidak didistribusikan bersama aplikasi.

AssetX dan Images2TGA menunjukkan bahwa kebutuhan penyuntingan aset dan konversi tekstur memang ada. Dokumen ini tidak mengklaim telah mengetahui atau menyalin algoritme internal kedua aplikasi tersebut. [Manual AssetX](https://trainz.shaneturner.co.uk/3rdparty/AssetX.pdf), [halaman Images2TGA](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/images2tga)

## 5. Cakupan dan prioritas

**P0** wajib untuk rilis inti. **P1** pengembangan sesudah inti stabil. **P2** perlu riset format/renderer/ekspor sebelum dijanjikan sebagai fitur rilis.

| Modul RailNexus | Tujuan | Prioritas |
| --- | --- | --- |
| Hubungkan Trainz | Memilih instalasi, profil data, dan kemampuan yang tersedia | P0 |
| Pustaka Aset | Mencari aset lokal dan memahami statusnya | P0 |
| Dependency & Modifikasi | Inventaris referensi, pilih peran, cari pengganti, tinjau dampak | P0 mesin umum; hornsound, enginesound, tracksound aturan yang ada, dan bogie |
| Pusat Perbaikan | Diagnosis, dependency, dan perbaikan yang dapat dibuktikan | P0 dasar; aturan lanjut P1 |
| Editor Aset | Formulir, pohon config, dan editor teks | P0 baca/pengubahan terbatas; lengkap P1 |
| Workshop Tekstur | Konversi gambar, alpha, metadata tekstur, batch | P0 dasar; PBR dan decoding khusus P1/P2 |
| Workshop Suara | Preview berkas A/B dan pemilihan set; pemeriksaan aturan suara | P0 preview dasar bila berkas dapat diakses; P1 simulasi set dan penyuntingan |
| Lihat Model | Preview geometri, material, attachment, animasi | P2 untuk renderer mandiri lengkap |
| Workshop Mesh | Konversi legacy dan operasi mesh terkontrol | P2 |
| Titik Pemasangan | Membuat atau mengubah attachment melalui jalur ekspor yang didukung | P2 |
| Gerak & Animasi | Pemeriksaan animasi serta pengaturan yang benar-benar didukung | P1 diagnosis; P2 authoring |
| Riwayat & Pemulihan | Backup, log pekerjaan, undo, pemulihan kegagalan | P0 |
| Preset & Batch | Mengulang perubahan pada banyak aset | P1 |

Semua jenis dependency yang dapat ditemukan boleh diperiksa dari rilis inti. Penggantian otomatis tersedia per profil peran: hornsound, enginesound, tracksound pada aturan yang sudah dikenali, dan bogie menjadi cakupan awal yang setara. Interior, pantograph, enginespec, produk/queue, mesh library, texture-group, dan library script memiliki tingkat dukungan tersendiri pada bagian 10. Penambahan aturan tracksound baru dan penanganan konflik yang kompleks menjadi P1.

### 5.1 Batas rilis inti

Rilis inti tidak mencakup editor 3D penuh, pembuatan animasi otomatis dari mesh apa pun, semua decoder format biner Trainz, pembongkaran konten terenkripsi, modifikasi database internal, atau perbaikan otomatis untuk seluruh jenis faulty. Tidak ada syarat fitur daring agar pengguna dapat mengedit aset lokal.

## 6. Peta layar dan bahasa antarmuka

Navigasi utama: **Pustaka · Dependency · Perbaikan · Workshop · Riwayat · Pengaturan**. Workshop berisi Aset/Config, Suara, Tekstur, Model/Mesh, Titik Pemasangan, serta Gerak. Semua berbagi aset aktif dan rencana perubahan.

| Layar | Isi utama | Tindakan utama |
| --- | --- | --- |
| Sambungkan | Instalasi, build, profil data, status koneksi | Hubungkan |
| Pustaka | Pencarian, filter, thumbnail, nama, status | Buka aset |
| Detail aset | Ringkasan, komponen, masalah, berkas | Mau ubah apa? |
| Pemilih komponen | Kandidat lokal, kondisi, preview bila tersedia | Pilih pengganti |
| Rencana perubahan | Slot lama → baru, dampak, backup | Terapkan & Selesai |
| Progres | Tahap aktif dan jumlah pekerjaan | Hentikan setelah tahap aman |
| Hasil | Berhasil/gagal, tindakan berikut, undo | Kembali / Uji di Trainz |
| Workshop tekstur | Daftar gambar, preview, pengaturan hasil | Konversi / Terapkan |
| Editor aset | Form, struktur, teks, masalah | Simpan rencana |

Contoh panel modifikasi:

```text
Aset terpilih: Lokomotif A
Status: Siap digunakan       Instalasi: TRS19 — Profil Utama

Mau mengerjakan apa?
[ Ganti komponen / dependency ] [ Atur suara ]
[ Tekstur & material ] [ Model & attachment ]
[ Periksa masalah ] [ Editor config ]

Rencana perubahan
Hornsound: Klakson A → Klakson B
Tracksound — aturan rel utama: Suara A → Suara B
Backup: akan dibuat otomatis

[Lihat detail]                         [Terapkan & Selesai]
```

Pilihan bersifat kontekstual. Aset suara menampilkan preview dan berkas suara; scenery dapat menampilkan mesh, tekstur, serta soundscript yang ditemukan; kendaraan menampilkan komponen yang benar-benar dimilikinya. Pengguna dapat memilih Tambah pengaturan jika profil mendukung field yang belum ada. Label lokasi atau slot hanya ditampilkan jika pemetaannya diketahui.

Istilah teknis memiliki terjemahan: “Komponen belum tersedia” untuk missing dependency, “Aset bermasalah” untuk faulty, “Masih terbuka untuk diedit” untuk open for edit. Label teknis asli dapat dibuka melalui detail.

### 6.1 Susunan UI bersama

Setiap modul memiliki halaman, tab, atau panel kerja khusus di dalam satu jendela RailNexus. Tidak perlu sembilan aplikasi atau sembilan jendela terpisah. Kerangka bersama terdiri dari:

- **Navigasi kiri:** Pustaka, Dependency, Perbaikan, Workshop, Riwayat, Pengaturan.
- **Bar konteks atas:** nama aset aktif, jenis, status, profil Trainz; KUID dapat dibuka atau disalin dari detail.
- **Area kerja tengah:** kontrol dan preview sesuai pekerjaan.
- **Ringkasan perubahan:** panel samping pada layar lebar atau bagian lipat pada layar kecil; menunjukkan sebelum → sesudah dan aset yang terkena perubahan.
- **Area tindakan bawah:** Kembali, Simpan draft bila diperlukan, dan satu tindakan utama. Pada aset terpasang, label akhirnya **Terapkan & Selesai**.

Pengguna melihat **Mau edit apa?** setelah memilih aset. Tombol tugas dipilih berdasarkan jenis dan kemampuan aset. Pilihan yang belum didukung diberi alasan singkat; tidak dibuat tampak aktif lalu gagal tanpa penjelasan.

Default antarmuka berbahasa Indonesia, dengan istilah komunitas seperti hornsound/tracksound ditampilkan sebagai keterangan kecil. Tidak ada keharusan membaca config untuk pekerjaan terpandu. Jendela harus tetap dapat digunakan pada 1280 × 720 dan penskalaan teks yang diperbesar; desain utama disiapkan pada 1440 × 900.

### 6.2 UI masing-masing fitur

| ID layar | Fitur / tampilan | Kontrol dan informasi wajib | Tindakan utama dan hasil |
| --- | --- | --- | --- |
| UI-01 | Sambungkan Trainz | Kartu instalasi terdeteksi, profil data, status pemeriksaan, pilih folder bila perlu | Hubungkan → pustaka aktif |
| UI-02 | Pilih mod/KUID | Pencarian nama/KUID, filter jenis/status, kartu atau tabel aset, thumbnail, aset terbaru | Pilih aset → halaman Mau edit apa? |
| UI-03 | Pusat edit aset | Nama/thumbnail/status, tombol tugas kontekstual, daftar perubahan draft | Pilih tugas → panel yang sesuai |
| UI-04 | Ganti dependency | Daftar peran/lokasi penggunaan, target sekarang, pencarian kandidat lokal, filter kecocokan, alasan kandidat tidak sesuai | Pilih pengganti → ringkasan perubahan |
| UI-05 | Hornsound | Pemutar lama/baru, daftar kandidat, play/stop, volume preview, informasi target | Gunakan suara ini → draft klakson |
| UI-06 | Enginesound | Pemilih set, daftar bagian suara yang terbaca, preview sampel, informasi kecocokan | Gunakan set ini → draft suara mesin |
| UI-07 | Tracksound | Daftar aturan, suara saat ini, konteks rel/kendaraan, pemilih suara dan preview, kondisi/prioritas di detail | Ganti suara aturan terpilih → draft tanpa mengubah kondisi lain |
| UI-08 | Komponen visual | Tab komponen yang ditemukan, slot/lokasi, kandidat, gambar/viewport bila tersedia, orientasi dan hasil pemeriksaan | Pilih komponen → draft interior/pantograph/bogie sesuai peran |
| UI-09 | Enginespec / fisika | Spesifikasi saat ini dan kandidat, perbandingan parameter yang dipahami, penjelasan efek, detail lanjutan | Gunakan spesifikasi → draft perubahan perilaku |
| UI-10 | Produk / muatan | Daftar queue, produk awal, jumlah, kapasitas, daftar produk/kategori diterima, konflik yang ditemukan | Simpan pengaturan muatan → draft |
| UI-11 | Library dan referensi lanjutan | Tab mesh/texture-group/script, pemilik, versi, path/nama simbolik, penggunaan dan tingkat dukungan | Pilih target yang didukung atau buka lokasi terkait di editor |
| UI-12 | Editor aset | Tab Formulir / Struktur / Teks / Perubahan, pencarian field, bantuan tag, penandaan masalah | Simpan rencana → draft bersama |
| UI-13 | Perbaiki mod | Kartu penyebab dan dampak, rantai dependency, opsi tindakan yang tersedia, hasil sebelum/sesudah | Terapkan perbaikan terpilih → hasil pemeriksaan |
| UI-14 | Tekstur: konversi | Dropzone, antrean file, preview gambar/checkerboard/kanal, peran, ukuran, format, benturan nama | Konversi ke draft atau simpan output folder |
| UI-15 | Tekstur: metadata | Daftar gambar dan pasangan .texture.txt, status pasangan, field peran/alpha, preview metadata | Buat/perbarui metadata terpilih → draft |
| UI-16 | Tekstur: PBR | Slot input map, label kanal output, pilihan nilai konstan, preview kanal dan ketidaksesuaian dimensi | Rakit map → output yang terverifikasi |
| UI-17 | Audio lokal / soundscript | Daftar WAV, pemutar, pengaturan pemutaran yang didukung, pemilih berkas pengganti | Simpan perubahan file/pengaturan → draft |
| UI-18 | Lihat model | Viewport, orbit/zoom/tampak tetap, pemilih LOD, daftar material/resource, overlay attachment bila didukung | Inspeksi atau buka pekerjaan model terkait |
| UI-19 | Mesh: konversi / bayangan | Daftar berkas dan format, pilihan target/metode, laporan dukungan, preview sebelum/sesudah | Konversi/hasilkan geometri → draft setelah pemeriksaan |
| UI-20 | Attachment | Viewport, daftar titik, nama/parent, koordinat/rotasi, gizmo, status koneksi ke komponen | Simpan titik → rencana ekspor dan penghubungan |
| UI-21 | Gerak & animasi | Daftar animasi, play/pause/timeline jika didukung, pemicu, pengaturan gerak terverifikasi, diagnosis | Simpan pengaturan atau tampilkan langkah uji |
| UI-22 | Batch / preset | Aset terpilih, filter cakupan, aksi/resep, tabel perubahan per aset, jumlah siap/perlu pilihan/dilewati | Jalankan pekerjaan → hasil per aset |
| UI-23 | Ringkasan, proses, hasil | Sebelum/sesudah, cakupan, status backup, progres tahap, hasil nyata, masalah dan tindak lanjut | Terapkan & Selesai → hasil; tersedia Undo bila dapat dilakukan |
| UI-24 | Riwayat / pemulihan | Daftar pekerjaan, aset dan perubahan, status snapshot, diff, konflik saat undo | Batalkan perubahan terpilih → transaksi pemulihan |
| UI-25 | Pengaturan | Profil Trainz, lokasi/ukuran backup, bahasa/tampilan, kemampuan yang diuji | Simpan pengaturan tanpa menghilangkan draft aktif |

Satu ID dapat berupa beberapa state atau tab dari halaman yang sama. Tabel menetapkan cakupan desain untuk seluruh produk; status P0/P1/P2 tetap mengikuti bagian 5, 10, dan 19. Menggambar UI fitur P2 tidak berarti formatnya sudah didukung.

### 6.3 Keadaan layar yang wajib didesain

Setiap UI memiliki keadaan normal serta keadaan khusus yang relevan: sedang memuat, belum ada data, tidak ada hasil pencarian, tidak didukung, input tidak valid, perubahan belum diterapkan, dan hasil pekerjaan. Layar transaksi juga memiliki keadaan berhasil, perbaikan sebagian, gagal/dipulihkan, konflik, dan hasil submit belum dapat dipastikan.

Tombol yang tidak bisa digunakan disertai alasan. Error menampilkan tindakan berikut seperti **Pilih pengganti**, **Sambungkan ulang**, atau **Buka detail**, bukan hanya kode. Jangan menghapus draft saat pengguna kembali, mencari kandidat lain, atau membuka bantuan.

### 6.4 Alur visual yang harus ditunjukkan

1. **Edit suara:** Pustaka → pilih mod → Mau edit apa? → Hornsound → dengarkan/pilih → ringkasan pada panel yang sama → Selesai → hasil.
2. **Edit suara rel:** Pustaka → pilih aset → Tracksound → pilih aturan → pilih suara → tinjau cakupan → Selesai → hasil.
3. **Perbaiki dependency:** pilih aset bermasalah → penyebab → pilihan perbaikan → ringkasan → terapkan → hasil pemeriksaan ulang.
4. **Edit tekstur:** pilih mod → Tekstur → pilih gambar → atur/preview → tambahkan ke draft → Selesai → hasil.
5. **Manual bila diperlukan:** pilih aset → Editor lanjutan pada lokasi relevan → ubah → lihat diff → Selesai.
6. **Pemulihan:** hasil/riwayat → pilih perubahan → periksa konflik → undo → hasil pemeriksaan ulang.

Alur paling sederhana menargetkan paling banyak empat keputusan utama setelah koneksi: aset, tujuan, pilihan perubahan, dan Selesai. Klik play/preview, pengetikan pencarian, serta kasus yang membutuhkan penyelesaian konflik dihitung terpisah. Target diuji pada pengguna, bukan dipaksakan dengan menyembunyikan informasi yang mereka perlukan.

### 6.5 Ketentuan mockup untuk AI

Sebelum implementasi aplikasi, AI wajib membuat peta layar, wireframe seluruh UI-01 sampai UI-25, lalu mockup rinci untuk alur utama. Hasil harus berupa visual yang dapat dilihat, misalnya gambar layar atau prototipe visual, dilengkapi nomor layar dan anotasi singkat. Tabel dan deskripsi teks saja tidak memenuhi ketentuan ini.

Desain harus konsisten: tipografi, warna, ukuran kontrol, jarak, status, dan posisi tombol utama sama lintas modul. Pilih satu arah visual yang terasa seperti aplikasi desktop sehari-hari, dengan ruang kerja yang jelas dan teks terbaca. Nama aset contoh boleh fiktif tetapi harus ditandai sebagai data contoh; jangan mengklaim inventaris tersebut berasal dari instalasi pengguna.

Sediakan tampilan 1440 × 900 dan contoh adaptasi 1280 × 720. Hindari satu gambar berisi puluhan layar kecil yang tidak bisa dibaca. Mockup P0 ditampilkan terperinci; wireframe P1/P2 cukup untuk menunjukkan letak dan alurnya, dengan penanda tahap pengembangan.

## 7. Alur pertama kali digunakan — CONNECT

**Tujuan:** pengguna masuk ke pustaka tanpa mengonfigurasi jalur teknis satu per satu.

1. Aplikasi mencari kandidat instalasi dari lokasi yang wajar dan menawarkan pencarian folder manual.
2. Pengguna memilih instalasi serta profil data aktif. Jika ada beberapa profil, masing-masing diberi nama jelas.
3. Pemeriksaan koneksi membaca identitas build dan kemampuan yang tersedia.
4. Jika Trainz perlu dibuka atau pengaturan developer diperlukan, layar memberikan petunjuk yang sesuai hasil pemeriksaan.
5. Aplikasi menampilkan hasil: terhubung, hanya baca, atau belum didukung.
6. Pengindeksan lokal dimulai. Pustaka tampil secara bertahap dan tidak menunggu semua thumbnail selesai.

**Aturan:** semua operasi terikat ke profil terpilih; aplikasi tidak boleh menulis ke profil lain. Berpindah profil ditunda selama transaksi tulis aktif. Profil build yang belum diuji dapat dibuka dalam mode terbatas.

**Kriteria penerimaan CONNECT-01:** dua instalasi dengan aset bernama sama tidak tercampur; KUID yang sama di profil berbeda tetap merupakan dua catatan lokal berbeda.

## 8. Pustaka aset — LIB

Pencarian mencakup nama, KUID, pembuat jika datanya tersedia, jenis aset, dan catatan pengguna. Filter meliputi terpasang, bermasalah, dependency hilang, masih diedit, favorit, dan baru diubah.

Setiap kartu/baris menampilkan nama, thumbnail atau ikon pengganti, jenis, status terakhir, serta waktu pemeriksaan. Hasil cache diberi penanda jika belum diperbarui.

Detail aset memiliki tab **Ringkasan, Komponen, Masalah, Berkas, Riwayat**. Bagian Komponen menyajikan relasi dalam bahasa pengguna; misalnya klakson, suara mesin, aturan suara rel, model bersama, kabin, muatan, library script, dan komponen mekanis. Pengguna dapat beralih antara daftar berdasarkan peran dan grafik dependency.

**Aturan LIB-01:** klasifikasi kandidat pengganti diperiksa dari konfigurasi yang relevan, bukan hanya nama atau kategori pencarian.

**Aturan LIB-02:** dependency langsung dan dependency turunan dibedakan. Grafik relasi menggunakan batas kedalaman, deteksi siklus, dan pemuatan bertahap.

**Aturan LIB-03:** thumbnail atau metadata yang belum tersedia tidak menghalangi pencarian aset. Membuka pustaka tidak otomatis membuka seluruh aset untuk diedit.

## 9. Mesin modifikasi aset dan dependency — MOD

### 9.1 Alur umum untuk seluruh peran

1. Pilih satu atau beberapa aset dari pustaka, atau buka folder proyek.
2. Pilih **Ganti komponen / dependency**, **Atur suara**, atau pekerjaan workshop lain.
3. RailNexus menginventaris referensi dan mengelompokkan perannya. Setiap entri menampilkan pemilik, lokasi, target saat ini, dan tingkat dukungan.
4. Pilih entri yang ingin diubah: satu field, satu aturan, satu slot, atau kelompok yang sesuai filter.
5. Pilih pengganti dari pustaka lokal. Pencarian mengutamakan jenis dan kebutuhan peran; nama hanya membantu pencarian.
6. Tampilkan preview yang sesuai: suara untuk audio, gambar untuk tekstur, informasi model untuk komponen visual, dan ringkasan kemampuan untuk library.
7. Jalankan pemeriksaan umum serta pemeriksaan khusus peran. Ketidakpastian tetap terlihat.
8. Susun satu rencana: sumber → target, lokasi perubahan, kondisi yang dipertahankan, aset lain yang terdampak, dan backup.
9. Tekan **Terapkan & Selesai**. Mesin transaksi menerapkan dan memeriksa hasil.
10. Tampilkan hasil tiap aset serta pilihan Uji di Trainz, Lihat detail, dan Batalkan perubahan.

**MOD-AC:** alur hornsound, enginesound, tracksound, dan bogie menggunakan mesin rencana/transaksi yang sama; fitur suara tidak bergantung pada implementasi wizard bogie.

### 9.2 Kelas referensi yang wajib dibedakan

| Kelas | Contoh | Cara produk memperlakukannya |
| --- | --- | --- |
| Referensi aset langsung | Hornsound, enginesound, interior | Pilih target dengan jenis dan kemampuan yang sesuai |
| Referensi di container | Aturan tracksound, slot bogey, queue | Pilih entri beserta kondisi di sekitarnya |
| Aset bersama + berkas di dalamnya | Mesh library dan path mesh | Periksa identitas library, versi, dan berkas yang dituju |
| Referensi script atau nama simbolik | Library pada kuid-table, include script | Pertahankan nama/kontrak; gunakan profil khusus atau manual |
| Berkas/resource lokal | WAV, gambar, metadata tekstur, mesh lokal | Gunakan workshop file; ini bukan otomatis dependency ber-KUID |
| Konten route/session | Referensi aset di data dunia/sesi | Diagnosis tersedia; penggantian instance memerlukan jalur editor/data yang didukung |

`kuid-table` mencatat dependency dan dapat menyediakan nama yang dipakai script. Karena itu, mengubah daftar tersebut saja belum tentu mengubah penggunaan aset. Nama entri juga tidak boleh dinomori ulang sembarangan. [KIND TrainzBaseSpec](https://online.ts2009.com/mediaWiki/index.php/KIND_TrainzBaseSpec)

### 9.3 Aturan perubahan umum

- Lokasi penggunaan merupakan target patch. Identitas KUID bukan perintah replace global.
- Pertahankan kondisi, orientasi, prioritas, nama simbolik, serta parameter lain di luar pilihan pengguna.
- Sinkronkan daftar dependency melalui analisis penggunaan dan aturan peran. Entri lama hanya boleh dibuang ketika ada bukti memadai bahwa tidak dipakai lagi; jika script/kontrak tidak diketahui, pertahankan dan tampilkan status belum dapat dipastikan.
- Pemilihan target yang sudah terpasang tidak mengubah isi target tersebut.
- Pemeriksaan jenis yang sama belum membuktikan kesesuaian perilaku, bentuk, script, atau sumber daya.
- Kandidat yang hilang, faulty, atau tidak dapat dipakai profil aktif disaring dari pilihan standar, dengan alasan yang dapat dibuka.
- Jika peran belum dikenali, tampilkan referensi dan lokasinya untuk pemeriksaan; jangan menebak semantik berdasarkan kemiripan nama.
- Aset yang tak bisa diubah melalui jalur resmi tetap dapat dibaca sejauh diizinkan; alur manual tidak berarti melewati pembatasan akses.

### 9.4 Dampak dan cakupan

Bedakan **mengganti referensi milik aset terpilih** dengan **mengedit isi dependency bersama**. Penggantian hornsound pada satu lokomotif tidak menyunting set suara yang dipakai lokomotif lain. Menyunting isi set suara bersama dapat memengaruhi seluruh pengguna set tersebut.

UI menawarkan cakupan yang relevan: aset ini, beberapa aset terpilih, atau buat varian baru ketika kloning didukung. Dampak ke route/session dijelaskan sebagai penggunaan identitas aset, bukan hitungan instance yang belum diperiksa. Varian baru tidak otomatis menggantikan penempatan lama.

### 9.5 Hubungan dengan editor dan workshop

Formulir, struktur config, workshop suara, workshop tekstur, dan modifikasi dependency menghasilkan ChangePlan yang sama. Pengguna boleh menambahkan beberapa perubahan lintas modul sebelum Selesai. Perubahan tidak boleh saling menimpa: misalnya mengubah hornsound lalu menyunting deskripsi tetap mempertahankan keduanya dalam draft.

Bagian 10 menetapkan spesialisasi peran, sedangkan bagian 21 menetapkan transaksi bersama.

## 10. Katalog peran dan alur khusus — ROLE

### 10.1 Tingkat dukungan

Setiap peran menampilkan salah satu status: **Otomatis teruji**, **Terpandu**, **Inspeksi/manual**, atau **Belum didukung**. Status ditentukan per build dan struktur aset. Daftar berikut merupakan target produk; tidak menyatakan semua fitur telah selesai.

| Peran | Lokasi/struktur yang diperiksa | Tindakan dan pemeriksaan | Tahap |
| --- | --- | --- | --- |
| Hornsound / klakson | Field hornsound kendaraan | Pilih aset suara, preview A/B, periksa kesehatan target | P0 |
| Enginesound / suara mesin | Field enginesound dan struktur set target | Pilih set, periksa format/bagian suara yang tersedia | P0 penggantian; P1 simulasi set |
| Tracksound / suara roda-rel | Aturan track-sound pada pemilik yang didukung | Pilih aturan, suara, kondisi pencocokan, dan prioritas | P0 aturan yang ada; P1 authoring kompleks |
| Bogie | Container slot kendaraan / bentuk legacy yang dikenali | Pilih slot, pertahankan orientasi, periksa jenis dan bentuk | P0 |
| Interior | Referensi kabin | Periksa kontrol, script, pemasangan dan kemampuan kabin | P1 terpandu |
| Pantograph | Referensi komponen pantograph | Periksa attachment, orientasi dan animasi | P1 terpandu |
| Enginespec | Referensi spesifikasi mesin/fisika | Cocokkan kelas kendaraan dan jenis mesin; bandingkan parameter | P1 terpandu |
| Muatan / produk | Queues, produk awal, daftar produk/kategori | Periksa kapasitas, isi awal, satuan/semantik produk, tampilan muatan | P1 terpandu |
| Mesh library | mesh-table, mesh-asset, path mesh | Pilih library dan mesh; periksa revisi, attachment, LOD | P1 referensi; decoder/konversi P2 |
| Texture-group / livery library | Aset kumpulan tekstur dan penggunaan oleh script | Periksa urutan/nama yang diharapkan serta resource tersedia | P1 inspeksi; otomasi profil khusus |
| Library script | script-include-table, kuid-table, kontrak script | Tinjau versi, nama include/class/API sebelum penggantian | P0 diagnosis; P1 profil khusus/manual |
| Suara lokal / soundscript | Daftar berkas dan pengaturan soundscript | Preview/ganti WAV, periksa trigger dan pengulangan | P1 workshop file |
| Jenis lain, termasuk track/scenery/signal/rule | Referensi yang ditemukan dan pemiliknya | Diagnosis umum, editor atau profil khusus yang teruji | P0 inspeksi; otomasi bertahap |

Trainz mendokumentasikan field hornsound, enginesound, interior, pantograph, dan enginespec pada kendaraan. Nama field tidak selalu sama dengan KIND target; misalnya enginespec mengacu pada konfigurasi mesin yang sesuai. [KIND Traincar](https://online.ts2009.com/mediaWiki/index.php/KIND_Traincar)

### 10.2 Hornsound

Alur: pilih aset → Atur suara → Klakson → cari aset lokal → dengarkan lama/baru → pilih → tinjau → Selesai. Jika sumber suara tidak dapat dibaca untuk preview, tampilkan keterbatasan itu; penggantian yang memenuhi syarat tidak harus bergantung pada renderer audio lengkap. Nama dan KUID target tetap dapat diperiksa.

Kriteria: hanya field klakson serta entri dependency yang memang perlu berubah yang disentuh; nama simbolik dipertahankan; jenis target salah ditolak; suara mesin dan aturan suara rel tetap utuh. Menambah field yang sebelumnya tidak ada hanya tersedia pada profil yang mendukungnya.

### 10.3 Enginesound

Alur: pilih kendaraan → Suara mesin → tampilkan set saat ini → pilih set baru → preview bagian yang tersedia → periksa kecocokan → Selesai. Jenis penggerak dan struktur set menjadi konteks pemeriksaan; aplikasi tidak mencocokkan hanya dari judul file atau nama aset.

Kriteria: memilih set tidak menimpa file WAV di dependency bersama. Mengedit file dalam set adalah tindakan terpisah yang menampilkan dampak pada pengguna lain. Preview satu sampel tidak diberi label simulasi seluruh rentang RPM.

### 10.4 Tracksound

Aturan tracksound memilih suara berdasarkan konteks rel/kendaraan, dapat memiliki pembatas rel, induk rel, dan bogie; nilai prioritas lebih rendah berarti kecocokan lebih diutamakan. [Track-sound container](https://online.ts2009.com/mediaWiki/index.php/%22track-sound%22_container)

1. Pilih aset → **Suara roda-rel**.
2. Identifikasi pemilik dan aturan yang tersedia. Jika sumbernya berada di dependency, tampilkan jalurnya.
3. Pilih aturan berdasarkan konteks pengguna, misalnya suara untuk jenis rel tertentu.
4. Lihat kondisi sekarang dan aset suara; cari kandidat lokal yang sesuai.
5. Dengarkan preview jika sumber dapat dibaca. Preview berkas tidak menyimulasikan seluruh pencocokan dalam game.
6. Untuk tindakan **Ganti suara pada aturan ini**, pertahankan filter dan prioritas. Perubahan kondisi menjadi pilihan eksplisit tersendiri.
7. Tampilkan dampak, terutama jika pemilik aturan adalah aset yang dipakai bersama.
8. Terapkan, periksa hasil, lalu uji pada pasangan kendaraan/rel yang relevan.

**P0:** mengubah target suara pada aturan yang sudah dikenali. **P1:** membuat aturan baru, mengatur kondisi, dan membantu diagnosis aturan bertabrakan. Konflik hanya dinyatakan tidak ditemukan dalam lingkup yang berhasil diperiksa; bukan jaminan tidak ada aturan lain di seluruh instalasi. Aturan yang dipilih script tak dikenal masuk pemeriksaan manual.

### 10.5 Komponen visual dan fisika

- **Interior:** periksa kesesuaian fungsi kabin dan script; tampilan serupa tidak membuktikan kontrol bekerja.
- **Pantograph:** periksa titik pemasangan serta perilaku animasi; identifikasi hasil yang belum dapat dipreview.
- **Bogie:** inventaris seluruh slot, pilih satu/beberapa, pertahankan orientasi; periksa gauge, tinggi, bentuk, dan animasi jika tersedia bukti. Label depan/belakang hanya jika diketahui.
- **Enginespec:** tampilkan perubahan perilaku yang dapat dipahami, seperti kelas penggerak dan parameter terkait; jangan otomatis menyetarakan spesifikasi semua kendaraan.

Semua menggunakan pemilih dan transaksi umum. Setiap peran memiliki fixture keberhasilan dan kegagalan sendiri.

### 10.6 Muatan, mesh library, dan tekstur bersama

**Muatan:** pilih queue → pilih produk/daftar produk yang diterima → tinjau kapasitas, produk awal, jumlah awal, serta representasi visual → terapkan. Queues mendukung produk dan kategori produk sebagai konsep berbeda, sehingga keduanya tidak saling diperlakukan sebagai pengganti tanpa pemeriksaan. [Queues container](https://online.ts2009.com/mediaWiki/index.php/%22Queues%22_container)

**Mesh library:** pilih entri model → pilih library → pilih berkas mesh dalam library itu → periksa path, revisi, dan struktur yang diperlukan → tinjau. `mesh-asset` dan nama/path `mesh` berhubungan; mengganti library saja dapat meninggalkan path yang tidak ada. [Mesh-table container](https://online.ts2009.com/mediaWiki/index.php/%22mesh-table%22_container)

**Texture-group:** inventaris kumpulan tekstur → periksa penggunaan/urutan yang diharapkan → pilih kandidat atau buka workshop → terapkan bila kontrak diketahui. Jenis ini menyediakan kumpulan tekstur; perilaku pemakaiannya perlu diperiksa pada aset pengguna. [KIND Texture-group](https://online.ts2009.com/mediaWiki/index.php/KIND_Texture-group)

### 10.7 Script, berkas lokal, dan route/session

**Library script:** tampilkan referensi, versi, dan bukti penggunaan. Pergantian hanya otomatis dengan profil kompatibilitas yang sudah teruji. Include lintas aset melibatkan konfigurasi dan file script; target berjenis library saja belum menjamin API atau class kompatibel. [Script Include Directive](https://online.ts2009.com/mediaWiki/index.php/Script_Include_Directive)

**Soundscript lokal:** buka daftar suara → dengarkan → pilih berkas baru → tinjau pengaturan terkait → simpan melalui transaksi file. Soundscript menggunakan file suara dan kondisi pemutaran; itu bukan selalu referensi ke aset hornsound/enginesound. [Sound Scripts](https://online.ts2009.com/mediaWiki/index.php/CCG/PFX%3A_Sound_Scripts)

**Route/session:** diagnosis missing dependency termasuk cakupan umum. Penggantian rel, scenery, kendaraan, atau rule yang tersimpan dalam data dunia/sesi memerlukan integrasi khusus. Mengubah kuid-table saja tidak diklaim menggantikan seluruh instance. Produk menampilkan batas dukungan per jenis operasi, dan membuka jalur editor yang sesuai saat otomasi belum tersedia.

## 11. Editor aset — EDIT

Modul ini memenuhi kebutuhan yang biasanya mendorong pengguna membuka AssetX: memahami konfigurasi, mengubah nilai, menelusuri struktur, dan menjalankan pekerjaan berulang. Alur RailNexus dimulai dari tugas dan menyediakan tiga cara melihat data yang sama.

### 11.1 Tiga tampilan

- **Formulir:** nama, deskripsi, thumbnail, komponen, dan pengaturan yang didukung.
- **Struktur:** container dan tag bertingkat untuk pengguna yang ingin memahami hubungan data.
- **Teks:** isi config dengan nomor baris, pencarian, penandaan masalah, dan perbandingan perubahan.

### 11.2 Alur penyuntingan

1. Buka aset → **Pengaturan lanjutan**.
2. Aplikasi membuat draft kerja dan mencatat keadaan awal.
3. Pengguna memilih field atau node. Panel bantuan menjelaskan fungsi dan sumber aturan.
4. Field referensi memiliki tombol **Pilih dari pustaka**; angka dan pilihan enum memakai kontrol yang sesuai.
5. Validasi lokal berjalan setelah perubahan berhenti sejenak, tidak mengganggu setiap ketikan.
6. Tab **Perubahan** memperlihatkan nilai lama dan baru.
7. **Simpan rencana** memasukkan perubahan ke antrean; belum menyentuh aset terpasang.
8. **Terapkan & Selesai** menjalankan transaksi bersama modul lain.

### 11.3 Aturan parser

- Mendukung struktur bertingkat, komentar, string bertanda kutip, token KUID, variasi spasi, dan akhir baris yang terdapat pada corpus uji.
- Menjaga tag yang tidak dikenal; tidak membuangnya hanya karena belum ada formulir.
- Menjaga isi di luar bagian yang sengaja diubah sejauh representasi sumber memungkinkan.
- Duplikasi atau struktur ambigu menghasilkan diagnosis; aplikasi tidak diam-diam memilih salah satu.
- Jika teks sementara tidak valid, tampilan formulir menunggu perbaikan dan tidak menulis ulang berdasarkan hasil parse lama.
- Referensi dalam script bukan pengganti otomatis yang sama dengan field KUID terstruktur.

### 11.4 Resep pekerjaan

Versi lanjutan menyediakan resep deklaratif seperti “ganti hornsound A pada kendaraan terpilih”, “ganti suara pada aturan tracksound tertentu tanpa mengubah kondisinya”, atau “buat metadata untuk gambar yang belum memilikinya”. Resep memiliki syarat, target, perubahan, pemeriksaan, dan preview. Impor resep tidak mengeksekusi perintah sistem.

**EDIT-AC:** membuka dan menyimpan draft tanpa perubahan menghasilkan konten identik; pengubahan satu field tidak menghilangkan container yang belum dikenal; mode teks dan form tidak saling menimpa draft invalid.

## 12. Pusat perbaikan — REPAIR

**Tujuan:** mengubah daftar error menjadi urutan pekerjaan yang jelas.

### 12.1 Alur pemeriksaan

1. Pilih **Periksa aset ini** atau buka filter aset bermasalah.
2. Aplikasi mengumpulkan kondisi aset, relasi dependency, config, berkas yang dapat diakses, dan laporan validator.
3. Masalah dikelompokkan berdasarkan penyebab. Contoh: Kendaraan A → Library B belum tersedia; atau Bangunan C → Mesh library D → berkas tekstur E hilang. UI membedakan relasi antar-aset dari berkas lokal yang hilang.
4. Kartu masalah menampilkan penyebab, bukti, dampak, dan tindakan yang tersedia.
5. Pengguna memilih tindakan; rencana perubahan disusun tanpa menulis aset.
6. Setelah diterapkan, jalankan pemeriksaan ulang terhadap sumber masalah dan aset induk yang relevan.

### 12.2 Empat kelas masalah

| Masalah | Arti | Tindakan produk |
| --- | --- | --- |
| Dependency belum terpasang | Identitas referensi diketahui, konten lokal belum tersedia | Cari sumber resmi yang tersedia, impor dari berkas pengguna, atau pilih pengganti |
| Dependency terpasang tetapi faulty | Referensi ada tetapi target gagal validasi | Buka diagnosis target; jangan menyebutnya sekadar belum diunduh |
| Config/berkas tidak konsisten | Ada bukti ketidaksesuaian lokal | Tawarkan perbaikan spesifik dengan preview |
| Script/mesh/format belum dipahami | Mesin perbaikan belum memiliki dasar yang cukup | Jelaskan batas pemeriksaan dan buka detail/manual |

### 12.3 Kebijakan perbaikan otomatis

Setiap aturan memiliki ID, versi, bukti pemicu, syarat berlaku, perubahan pasti, metode pemulihan, dan pemeriksaan sesudahnya.

Contoh layak otomatis setelah terbukti: menambahkan entri dependency yang hilang ketika penggunaan struktural dan kunci yang diperlukan jelas; memperbaiki metadata tekstur ketika gambar sumber dan perannya diketahui. Entri bernama dalam kuid-table tidak dianggap sampah hanya karena tidak ditemukan pada tag config lain: script mungkin menggunakannya.

Contoh perlu pilihan: dua gambar sama-sama mungkin menjadi sumber tekstur; dependency hilang tetapi ada beberapa kandidat pengganti; perubahan yang memengaruhi aset bersama.

Contoh tidak ditebak: mesh hilang tanpa sumber, script terenkripsi, KUID tidak dikenal tanpa kandidat yang dapat dibuktikan, atau kerusakan file biner di luar decoder yang diuji.

Tombol **Terapkan perbaikan terpilih** tidak menjanjikan semua faulty akan sembuh. Download dependency hanya ditandai selesai setelah pemasangan dan pemeriksaan ulang. RailNexus tidak memerlukan pengguna memasukkan password Trainz ke aplikasinya.

## 13. Workshop tekstur — TEX

Modul ini menggabungkan kebutuhan konversi gambar seperti Images2TGA dan pembuatan metadata seperti TextureTXT dalam alur baru. Konversi gambar biasa dan pembacaan `.texture` biner diperlakukan sebagai kemampuan berbeda.

### 13.1 Tiga jenis berkas

Gambar sumber seperti TGA/PNG berisi piksel. `.texture.txt` berisi pengaturan tekstur yang dapat diedit. `.texture` terkompilasi adalah berkas yang memerlukan pembaca format sesuai variannya. Metadata tidak dapat menghasilkan gambar yang hilang. [Dokumentasi Texture file](https://online.ts2009.com/mediaWiki/index.php/Texture_file)

### 13.2 Alur A — gambar menjadi tekstur siap digunakan

1. **Masukkan gambar.** Drag-and-drop file/folder atau buka Tekstur dari detail aset.
2. **Inventarisasi.** Tampilkan nama, ukuran, format terdeteksi, alpha, serta hubungan ke metadata yang ada.
3. **Pilih tujuan.** Gambar warna, normal, parameter, mask/transparansi, atau gambar umum. Tebakan nama hanya menjadi saran.
4. **Pilih profil output.** Misalnya TGA kompatibilitas atau PNG pada profil build yang sudah diuji.
5. **Tinjau alpha.** Lihat di atas checkerboard dan latar terang/gelap; pilih pertahankan, buang secara sengaja, atau gunakan mask terpisah.
6. **Tinjau ukuran.** Pertahankan ukuran sebagai default. Jika profil menolak ukuran, tawarkan resize atau padding dengan preview; jangan mengubah rasio diam-diam.
7. **Tinjau nama.** Daftar sumber → output menandai benturan nama dan file yang sudah ada.
8. **Konversi ke staging.** Sumber belum disentuh. Semua output diperiksa dengan membacanya kembali.
9. **Buat metadata.** Hanya tag yang sesuai peran dan profil yang dikeluarkan.
10. **Simpan atau terapkan.** Folder bebas memakai Simpan hasil; aset terpasang memakai transaksi dan submit.

Contoh hasil yang mudah dipahami:

```text
Sumber: badan.png, 2048 × 2048, memiliki alpha
Peran: gambar warna dengan transparansi
Output: badan.tga + badan.texture.txt
Status: menunggu penerapan
```

### 13.3 Mesin konversi

- Decoder membaca isi berkas, bukan percaya pada ekstensi saja.
- Orientasi, kanal warna, alpha, dan jenis data diperiksa sebelum penulisan.
- Output tanpa alpha dapat menggunakan TGA 24-bit; output yang memerlukan alpha menggunakan 32-bit.
- Operasi warna tidak diterapkan otomatis pada normal map atau data parameter.
- Pengubahan ukuran normal map memerlukan metode yang menjaga vektor normal; jika belum tersedia, fitur itu ditolak untuk normal map.
- Konversi JPG ke TGA tidak diberi label peningkatan detail. Informasi yang hilang akibat kompresi sumber tidak dipulihkan.
- Profil alpha membedakan transparansi gambar dari alpha yang menyimpan data material.
- Sumber di luar aset disalin ke staging yang terkelola; output tidak ditulis keluar folder tujuan melalui nama file relatif yang tidak valid.

### 13.4 Alur B — membuat atau memperbaiki `.texture.txt`

1. Pilih gambar atau folder aset.
2. Temukan metadata yang sudah ada dan identifikasi gambar yang belum memiliki pasangan.
3. Pengguna memilih peran untuk setiap kelompok.
4. Tampilkan preview pasangan file dan properti hasil.
5. Metadata lama dibandingkan; pengaturan khusus yang tidak dikenal dipertahankan sampai pengguna memilih penggantian yang jelas.
6. Terapkan hanya baris terpilih, periksa referensi gambar, lalu validasi aset jika terpasang.

Prinsip teknis: file `.texture.txt` menggunakan pasangan key/value; pemakaian resource dalam konfigurasi tertentu menggunakan nama `.texture`. Properti, ukuran, dan sumber gambar harus mengikuti profil format tujuan, bukan selalu dokumentasi terbaru. [Format dan sumber gambar Trainz](https://online.ts2009.com/mediaWiki/index.php/Texture_file)

### 13.5 Alur C — mengambil gambar dari `.texture` yang didukung

1. Baca header dan identifikasi varian format tanpa mengubah sumber.
2. Jika decoder belum tersedia, tampilkan format tidak didukung dan tawarkan mencari gambar sumber.
3. Jika didukung, decode ke staging dan tampilkan resolusi, alpha, serta level mip yang tersedia.
4. Pengguna memilih hasil ekspor; metadata hanya diisi dari informasi yang diketahui.
5. Simpan dengan nama baru. Penggantian dalam aset memerlukan rencana terpisah dan pemeriksaan.

Hasil decode tidak diklaim identik dengan gambar asli sebelum kompresi. Dukungan `.texture` lama, varian baru, dan format terproteksi harus dicatat terpisah dalam matriks codec. Kemampuan ini P2 sampai tersedia pembaca mandiri yang dapat diuji dan digunakan sesuai lisensi.

### 13.6 Alur D — merakit tekstur PBR

1. Pilih material/profil tujuan yang didukung.
2. Masukkan albedo, normal, dan map parameter terpisah.
3. Tetapkan peran setiap gambar secara eksplisit; aplikasi menampilkan kanal hasil.
4. Periksa dimensi, ruang warna, dan kanal yang kosong.
5. Rakit output, baca kembali setiap kanal, lalu tampilkan preview.
6. Terapkan ke aset hanya bila hubungan material ke resource dapat diperbarui melalui jalur yang didukung.

Untuk `m.pbrmetal`, dokumentasi menjelaskan albedo, normal dengan kemungkinan height pada alpha, dan parameter RGBA berisi emissive, roughness, ambient occlusion, metallic. Transparansi tidak otomatis muncul hanya karena albedo diberi alpha. [Material m.pbrmetal](https://online.ts2009.com/mediaWiki/index.php/M.pbrmetal)

Map yang tidak diberikan ditandai “nilai konstan pilihan pengguna” atau “belum tersedia”; aplikasi tidak membuat seolah-olah data tersebut hasil pengukuran. Pengepakan map tidak otomatis mengubah material mesh legacy menjadi PBR.

### 13.7 Konversi massal

Tabel per berkas: sumber, tujuan, profil, alpha, ukuran, benturan, status. Pilihan benturan: lewati, simpan nama baru, atau ganti dengan backup. Menjalankan ulang pekerjaan dengan input dan pengaturan yang sama mendeteksi hasil yang sudah benar. Satu gambar rusak tidak menghapus hasil gambar lain.

**TEX-AC:** gambar uji warna/alpha dibaca kembali tanpa kanal tertukar; normal dan parameter tidak mengalami koreksi warna otomatis; sumber tetap utuh; metadata menunjuk berkas yang ada; benturan tidak ditimpa tanpa pilihan yang tercatat.

## 14. Preview suara — AUDIO

1. Pengguna membuka komponen suara dari kendaraan atau pustaka.
2. Aplikasi menampilkan berkas dan bagian suara yang dapat dibaca.
3. Pengguna memilih lama/baru, play/pause, volume preview, dan bagian loop yang tersedia.
4. Jika struktur set diketahui, sediakan pergantian sampel menurut tingkat/RPM yang didukung profil.
5. Pilihan pengganti kembali ke rencana modifikasi; mendengarkan saja tidak mengubah aset.

Preview sampel dibedakan dari simulasi audio Trainz. Perubahan volume preview tidak menyunting volume aset. Normalisasi, trim, dan ekspor audio merupakan fitur lanjutan dengan salinan output dan format tujuan yang diuji.

**AUDIO-AC:** suara A/B dapat dihentikan; berpindah aset menghentikan playback lama; format tidak didukung memberi pesan yang jelas; hasil preview tidak diklaim identik dengan suara dalam game.

## 15. Preview model — VIEW

Alur: pilih aset → kumpulkan berkas yang dapat diakses → pilih LOD/model → orbit/zoom atau tampak depan/samping/atas → tampilkan material, attachment, dan animasi yang didukung.

Untuk model gabungan seperti kendaraan dengan beberapa komponen atau scenery dari mesh library, perakitan hanya dilakukan jika hierarki, posisi pemasangan, orientasi, dan skala dapat dibaca dengan benar. Jika hanya mesh tunggal yang dapat dibaca, labelnya **Preview mesh tunggal**.

Kemampuan ditampilkan per format: geometri, tekstur, material, skinning, animasi, LOD, dan attachment. “Bisa membuka file” tidak cukup untuk mengklaim seluruh fitur format didukung.

Rilis awal menyediakan thumbnail dan jalur preview Trainz jika kemampuan tersebut lolos uji integrasi. Renderer mandiri lengkap menjadi proyek terpisah. Shader sederhana harus diberi label sebagai preview perkiraan.

**VIEW-AC:** orientasi dan skala fixture benar; resource yang hilang ditandai; material yang belum didukung tidak ditampilkan seolah akurat; viewer tidak menulis kembali mesh saat dibuka.

## 16. Workshop mesh — MESH

### 16.1 Konversi legacy

Tujuan yang menyerupai kebutuhan PM2IM dirancang sebagai pekerjaan mandiri:

1. Pilih mesh dan salinan aset uji.
2. Identifikasi format, versi, material, hierarki, skinning/animasi, dan fitur yang belum didukung.
3. Tampilkan rencana konversi serta kemungkinan perubahan visual.
4. Konversi hanya jika pembaca dan penulis format memenuhi syarat untuk berkas tersebut.
5. Bandingkan jumlah elemen, bounding box, material, attachment, serta data animasi.
6. Perbarui referensi dalam draft, lalu lakukan impor/validasi dan pemeriksaan visual.

Mengganti ekstensi bukan konversi. Jika ekspor format target belum memiliki jalur yang teruji, fitur berstatus riset. Perubahan specular/material adalah tindakan terpisah dengan preview, bukan bonus otomatis setiap konversi.

### 16.2 Bayangan

Kebutuhan yang sejenis QuickShadows ditampung sebagai **Siapkan geometri bayangan legacy**. Pengguna memilih profil aset yang memang membutuhkan keluaran tersebut, memilih tingkat penyederhanaan, memeriksa siluet, lalu mengekspor melalui format yang didukung.

Aplikasi tidak menambahkan shadow mesh terpisah ke semua aset modern. Keputusan diambil berdasarkan profil material/mesh/build yang diuji.

**MESH-AC:** converter menolak fitur yang tidak mampu dipertahankan; sumber utuh; hasil struktur dan visual dibandingkan; aset yang lolos validasi tetapi rusak visual belum memenuhi kriteria rilis.

## 17. Titik pemasangan — ATTACH

Kebutuhan yang sejenis Attachment Maker menjadi alur visual:

1. Pilih aset dan mesh sumber yang didukung.
2. Pilih **Titik pemasangan** → tambah atau pilih titik.
3. Masukkan nama, posisi, dan rotasi; tampilkan sumbu serta gizmo.
4. Tampilkan komponen contoh bila tersedia untuk memeriksa orientasi.
5. Periksa nama duplikat, satuan, parent, dan dukungan penggunaan oleh aset.
6. Ekspor mesh pendukung atau sumber yang diperbarui melalui jalur yang teruji.
7. Perbarui draft config jika diperlukan, lalu submit dan uji di Trainz.

Format final ditentukan setelah riset ekspor; jangan menjanjikan penambahan attachment ke setiap mesh biner. Untuk sumber FBX, proses konversi ke format Trainz mengikuti jalur impor yang didukung Trainz. [FBX file format](https://online.ts2009.com/mediaWiki/index.php/FBX_file_format)

**ATTACH-AC:** koordinat dan parent sesuai fixture; titik tidak hilang setelah impor; titik yang dibuat tetapi tidak dipakai konfigurasi ditampilkan sebagai belum terhubung.

## 18. Gerak dan animasi — MOTION

Kebutuhan gerak yang dahulu ditangani utilitas terpisah ditempatkan pada panel aset, dengan pilihan hanya untuk profil yang dapat dibuktikan mendukungnya.

Alur umum: pilih aset → **Gerak & Animasi** → inventaris animasi serta pemicunya → periksa resource/hierarki yang dapat dibaca → preview jika didukung → uji perilaku di Trainz. Kasus mencakup komponen kendaraan, pantograph, pintu, scenery beranimasi, serta gerakan suspensi. Pemicu dari script ditandai terpisah dari loop otomatis; pemutaran preview tidak membuktikan pemicu dalam game bekerja. Pengaturan animasi mesh didokumentasikan pada [mesh-table](https://online.ts2009.com/mediaWiki/index.php/%22mesh-table%22_container).

Untuk bogie, pemeriksaan mencakup keberadaan berkas animasi, nilai pengaturan terkait, serta keterkaitan ke mesh yang dapat dianalisis. Trainz mendokumentasikan `anim.kin` dan `animdist` untuk animasi bogie. Mengganti referensi bogie tidak sekaligus membuat animasi yang belum ada. [KIND Bogey](https://online.ts2009.com/mediaWiki/index.php/KIND_Bogey)

Alur: pilih bogie → **Periksa animasi roda** → tampilkan bukti dan masalah → preview jika decoder mendukung → uji berhenti, maju, mundur, dan perubahan LOD di Trainz.

Pengaturan sway ditambahkan hanya ketika mekanisme dan parameter untuk build tujuan telah diverifikasi. Tidak ada penyisipan script goyang generik ke semua kendaraan. Membuat rig/animasi baru memerlukan workflow sumber 3D dan berada di luar rilis inti.

## 19. Sembilan kebutuhan utilitas dalam satu aplikasi RailNexus

**Kesembilan utilitas masuk ke cakupan rancangan.** Pengguna mengakses fungsinya melalui modul RailNexus dengan pustaka, draft, antrean pekerjaan, backup, dan riwayat yang sama. Mesin tiap modul dikembangkan sendiri. Tabel berikut menjelaskan alur RailNexus yang dirancang, bukan klaim algoritme internal aplikasi asal atau kemampuan yang sudah selesai dibuat.

### 19.1 Pemetaan lengkap dan alur

| Referensi kebutuhan | Modul RailNexus | Input → langkah pengguna → hasil | Cakupan rilis |
| --- | --- | --- | --- |
| [AssetX](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/assetx) | Editor Aset + Pusat Perbaikan + Resep | Aset/folder → baca form atau pohon config → ubah nilai/pilih dependency/jalankan resep → diff → terapkan → laporan dan undo | P0 editor terbatas/diagnosis; P1 editor penuh, resep dan alat eksternal pilihan pengguna. Detail bagian 11–12 |
| [Soundz](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/soundz) | Workshop Suara | Aset/set suara → temukan bagian yang dapat dibaca → dengarkan A/B → pilih atau edit salinan → terapkan perubahan referensi/file sesuai tujuan | P0 preview dasar horn/engine/track yang dapat diakses; P1 preview set dan editing. Detail bagian 10, 14 |
| [Trainz Mesh Viewer 2](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/trainz-mesh-viewer-2) | Lihat Model | Aset/mesh → identifikasi format → orbit/zoom/pilih tampak → lihat material, LOD, attachment, animasi yang didukung → hasil inspeksi | P0 thumbnail/jalur preview Trainz teruji; P2 renderer sendiri. Detail bagian 15 |
| [Train Swayer Utility](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/train-swayer-utility) | Gerak & Animasi | Kendaraan → periksa mekanisme gerak yang tersedia → atur parameter yang didukung → preview/uji → simpan | P1 diagnosis dan pengaturan profil teruji; P2 authoring tambahan. Detail bagian 18 |
| [QuickShadows](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/quickshadows) | Workshop Mesh: Bayangan | Mesh + profil legacy → pilih kebutuhan dan penyederhanaan → tinjau siluet → ekspor → hubungkan ke draft → uji | P2 generator sendiri, hanya untuk kebutuhan profil yang sesuai. Detail bagian 16.2 |
| [Images2TGA](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/images2tga) | Workshop Tekstur: Konversi | Gambar atau .texture yang didukung → periksa format/alpha/ukuran → preview → konversi staging → metadata → simpan atau terapkan | P0 gambar umum; P1 PBR; P2 decoder .texture per varian. Detail bagian 13 |
| [PM2IM](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/pm2im) | Workshop Mesh: Konversi Legacy | Mesh .pm → inventaris struktur/material/animasi → periksa dukungan penulis .im → konversi → bandingkan → perbarui draft → uji | P2 setelah pembaca/penulis sendiri atau pustaka berlisensi lolos uji. Detail bagian 16.1 |
| [Attachment Maker](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/attachment-maker) | Titik Pemasangan | Aset/mesh → pilih titik → nama/posisi/rotasi/parent → preview → ekspor mesh pendukung melalui jalur teruji → hubungkan dan uji | P2; tidak menjanjikan edit langsung seluruh format biner. Detail bagian 17 |
| [TextureTXT](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/texturetxt) | Workshop Tekstur: Metadata | Gambar/folder → temukan metadata yang hilang/bermasalah → pilih peran dan alpha → tinjau file pasangan → buat/perbarui .texture.txt | P0 metadata dasar, P1 pengaturan lanjutan. Detail bagian 13.4 |

Sway dan shadow legacy dipertahankan sebagai kebutuhan yang bisa ditangani bila relevan. Keberadaan fitur pada utilitas lama tidak menjadi alasan menerapkannya pada setiap aset TRS19 ke atas. Dokumentasi Train Swayer sendiri menyebut adanya sway bawaan T:ANE; RailNexus memilih mekanisme sesuai build, bukan menambahkan dua sistem gerak sekaligus. [Train Swayer Utility](https://trainz.shaneturner.co.uk/tutorials/index.php/home/utilities/pevsoft-trainz-tools/train-swayer-utility)

### 19.2 Contoh kerja lintas modul

1. Pilih kendaraan dari Pustaka.
2. Buka Workshop Suara, dengarkan hornsound baru, lalu tambahkan penggantian ke draft.
3. Buka Dependency, pilih aturan tracksound yang ada, lalu ubah hanya target suaranya.
4. Buka Workshop Tekstur, perbaiki metadata gambar yang diketahui sumbernya.
5. Editor Aset menunjukkan seluruh perubahan yang dikumpulkan, termasuk lokasi dan alasannya.
6. Tekan Selesai sekali untuk menjalankan rencana pada aset yang terlibat, dengan transaksi dan hasil per aset.
7. Riwayat menampilkan perubahan suara, dependency, dan tekstur dalam satu pekerjaan yang dapat ditinjau.

Jika rencana melibatkan dependency bersama, pengguna melihat pemilik perubahan dan cakupannya sebelum penerapan. Jika suatu format belum didukung, pekerjaan lain yang independen tetap dapat disiapkan; rencana tidak diam-diam menghilangkan langkah yang gagal.

### 19.3 Integrasi editor eksternal pilihan pengguna

Kebutuhan membuka aplikasi lain, seperti editor audio/gambar/3D, tetap tersedia sebagai kenyamanan P1. Pengguna memilih aplikasi terpasang → RailNexus menyediakan salinan kerja → buka file → setelah kembali, baca ulang perubahan → validasi → masukkan ke draft. Editor eksternal tidak menulis langsung ke aset terpasang melalui alur ini.

Fitur inti tidak mewajibkan instalasi salah satu dari sembilan utilitas. Resep RailNexus menggunakan format dan mesin sendiri; kompatibilitas script AssetX tidak diasumsikan.

## 20. Batch dan preset — BATCH

1. Pengguna memilih beberapa aset dari pustaka.
2. Pilih tindakan dan batas cakupan, misalnya hornsound yang memakai aset A atau target suara pada aturan tracksound bernama tertentu. Pemilihan dilakukan berdasarkan peran dan lokasi, bukan seluruh token KUID yang sama.
3. Aplikasi membentuk rencana per aset dan memisahkan siap diproses, perlu pilihan, dan dilewati.
4. Pengguna melihat ringkasan jumlah dan contoh detail sebelum menjalankan.
5. Antrean memproses transaksi satu per satu untuk satu profil.
6. Hasil menampilkan setiap aset dan tindakan pemulihannya.

Batch tidak diklaim sebagai transaksi tunggal yang atomik. Jika aset kelima gagal, empat aset sebelumnya dapat sudah berhasil. Pengguna dapat menghentikan antrean, melanjutkan aset lain yang independen, atau memulihkan pekerjaan yang telah selesai.

Preset menyimpan maksud perubahan, identitas target bila diperlukan, versi aturan, dan syarat kompatibilitas. Membuka preset pada profil lain wajib menyelesaikan ulang kandidat yang belum tersedia. Preset tidak membawa berkas mod pihak lain secara otomatis.

## 21. Transaksi, backup, dan undo — TX

### 21.1 Dasar integrasi yang diketahui

Dokumentasi TrainzUtil menyediakan operasi pencarian/status, membaca config, edit, commit, validate, relasi dependency, impor, dan ekspor. Dokumentasi juga mencatat variasi keluaran antarbuild, kebutuhan pengaturan/koneksi pada sebagian versi, serta bahwa revert tidak memulihkan perubahan yang sudah di-commit. Halamannya belum diperbarui untuk TRS22. Karena itu, rancangan memakai adapter per build dan backup milik RailNexus. [TrainzUtil](https://online.ts2009.com/mediaWiki/index.php/TrainzUtil)

### 21.2 Tahapan transaksi yang dirancang

| Tahap | Pekerjaan | Jika gagal |
| --- | --- | --- |
| Draft | Susun patch dan output staging | Simpan draft; aset belum berubah |
| Periksa ulang | Baca keadaan terbaru, kemampuan, hak edit, dan konflik | Hentikan penerapan dengan alasan |
| Kunci pekerjaan | Ambil kunci aplikasi untuk profil/aset | Tunggu atau batalkan antrean |
| Snapshot | Simpan preimage dan manifest pemulihan | Jangan lanjut menulis |
| Siapkan edit | Dapatkan area edit melalui jalur yang didukung | Jangan membuat asumsi lokasi |
| Verifikasi awal | Bandingkan keadaan terkini dengan dasar draft | Minta pengguna meninjau konflik |
| Tulis | Terapkan perubahan terpilih dan catat berkas baru | Pulihkan hanya perubahan transaksi ini |
| Submit | Minta Trainz menyelesaikan perubahan | Periksa keadaan nyata sebelum mencoba ulang |
| Periksa hasil | Baca validasi dan status terbaru | Diagnosis atau pemulihan |
| Tutup pekerjaan | Simpan hasil, hash, log, dan tautan backup | Catat pekerjaan untuk rekonsiliasi |

Snapshot berisi byte asli setiap berkas yang akan diubah/dihapus, daftar berkas yang dibuat, metadata profil/aset, serta hash keadaan sebelum dan sesudah. Operasi yang dapat mengubah banyak keluaran melalui importer memakai snapshot folder/sumber yang memadai atau ekspor pemulihan yang telah diuji. Backup config saja tidak cukup untuk transaksi tekstur/mesh.

### 21.3 Konflik dan pemulihan

- Aset yang sebelumnya sudah terbuka untuk diedit tidak otomatis diambil alih. Tampilkan keadaan dan tawarkan alur draft/manual yang mempertahankan pekerjaan tersebut.
- Sebelum menulis, bandingkan lagi berkas yang relevan. Kunci aplikasi tidak menjamin Content Manager atau editor lain ikut terkunci.
- Sesudah commit gagal/timeout, status adalah **Belum dapat dipastikan** sampai rekonsiliasi selesai. Jangan mengulang operasi mutasi secara membabi buta.
- Pemulihan dilakukan hanya jika keadaan masih sesuai perubahan yang dimiliki transaksi. Jika ada edit eksternal, tampilkan konflik.
- Undo membuat transaksi baru untuk mengembalikan snapshot; sesudahnya aset tetap diperiksa kembali.
- Pembatalan saat submit menunggu batas aman. Menghentikan UI tidak dianggap membatalkan pekerjaan yang sudah diterima Trainz.
- Setelah aplikasi crash, jurnal membantu membedakan belum ditulis, sudah ditulis, sedang submit, dan selesai.

### 21.4 Aset yang memang sudah faulty

Untuk tugas perbaikan, bandingkan masalah sebelum dan sesudah. Berkurangnya error dapat disebut **Perbaikan sebagian**, tetapi bukan **Siap digunakan** jika masih faulty. Jika perubahan menambah masalah baru yang berkaitan, tawarkan atau jalankan pemulihan sesuai kebijakan transaksi yang sudah ditampilkan.

### 21.5 Penyimpanan riwayat

Pengguna melihat waktu, aset, perubahan, hasil, dan ukuran backup. Backup untuk transaksi belum selesai tidak dihapus oleh pembersihan otomatis. Jika ruang tidak cukup untuk snapshot baru, penulisan aset tidak dimulai. Penghapusan backup lama menunjukkan hilangnya kemampuan undo untuk pekerjaan terkait.

## 22. Batas otomatisasi dan keadaan khusus

| Keadaan | Perilaku yang disyaratkan |
| --- | --- |
| Trainz belum tersedia | Mode folder tetap dapat dipakai; fitur terpasang menampilkan langkah koneksi |
| Build belum dikenali | Pemeriksaan kemampuan; fitur tulis dibatasi sampai adapter lolos uji |
| Dependency tidak ditemukan | Tampilkan KUID dan asal referensi; jangan mengarang aset pengganti |
| Aset packaged/builtin/payware | Baca kondisi akses aktual; modifikasi/kloning hanya melalui jalur yang memang diizinkan |
| Parser tidak memahami struktur | Pertahankan sumber; hentikan otomasi pada bagian tersebut |
| Pengguna mengganti profil saat antrean aktif | Selesaikan/batalkan aman sebelum pindah |
| File sumber berubah di luar aplikasi | Rencana menjadi kedaluwarsa dan perlu ditinjau ulang |
| Output converter gagal dibaca ulang | Jangan terapkan output itu |
| Validasi lolos tetapi roda/posisi salah | Tandai perlu uji visual; sediakan undo |

## 23. Rancangan teknis awal

**Pilihan teknologi:** C# untuk logika, WPF untuk antarmuka Windows, .NET 10 LTS untuk target runtime, dan SQLite untuk indeks/cache/riwayat. Ini keputusan rancangan, bukan persyaratan memasang database server. Status LTS .NET mengacu pada [kebijakan dukungan Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Distribusi ditargetkan sebagai installer atau paket aplikasi mandiri yang menyertakan runtime. Library gambar/audio/rendering baru dipilih setelah uji format, performa, dan lisensi. Tidak ada keputusan yang menganggap seluruh codec atau renderer sudah tersedia hanya karena memilih C#.

### 23.1 Komponen

| Komponen | Tanggung jawab |
| --- | --- |
| Desktop UI | Layar, navigasi, formulir, progres, aksesibilitas |
| Application Core | Menjalankan use case dan menyusun rencana perubahan |
| Config Engine | Membaca struktur, mengedit lokasi spesifik, menghasilkan diff |
| Dependency Service | Relasi antar-aset dan resource lokal, kandidat, dampak, bukti penggunaan |
| Role Registry | Profil peran untuk field/container/library/file; syarat kandidat, patch, dan validasi |
| Trainz Adapter | Menjalankan operasi pada instalasi terpilih dan menormalkan hasil |
| Transaction Service | Jurnal, snapshot, penulisan, konflik, pemulihan |
| Texture Pipeline | Decoder/encoder, kanal, metadata, batch |
| Audio/Model Preview | Kemampuan preview berdasarkan format |
| Rule Catalog | Syarat dan tindakan diagnosis/perbaikan yang diberi versi |
| Local Store | Cache, indeks pencarian, pengaturan dan riwayat |

### 23.2 Batas integrasi

Application Core memakai operasi konseptual seperti ReadAsset, ReadConfig, ReadDependencies, BeginEdit, Submit, Validate, dan ImportFolder. Adapter menerjemahkannya ke kemampuan nyata build terpilih. Nama ini adalah kontrak internal RailNexus, bukan klaim nama API resmi Trainz.

Tidak ada penulisan langsung ke berkas database internal Trainz. Parser keluaran perintah harus diuji dengan contoh hasil nyata, termasuk keluaran error, bahasa berbeda, dan jalur dengan spasi. Hasil yang tidak dikenali menjadi status tidak diketahui, bukan sukses.

## 24. Model data minimum

| Entitas | Data inti |
| --- | --- |
| InstallationProfile | ID profil, lokasi program/data, build, kemampuan teruji |
| AssetRecord | Profil, identitas KUID dan revisi, nama, kind, status, waktu pembacaan |
| DependencyEdge | Aset asal/target, lokasi referensi, nama simbolik, peran, langsung/turunan, asal bukti |
| ResourceReference | Pemilik, path/resource lokal, tipe file, hubungan ke material/mesh/suara, status |
| RoleProfile | Jenis pemilik, pola lokasi, jenis/kontrak target, kondisi patch, validator, versi dukungan |
| EditableSlot | Jalur node, nilai saat ini, tipe kandidat, pengaturan yang harus dipertahankan |
| ChangePlan | Tujuan pengguna, aset terlibat, precondition, patch, dampak, versi aturan |
| Transaction | ID, fase, waktu, snapshot, hash, keluaran submit, status rekonsiliasi |
| FileSnapshot | Jalur relatif, keadaan sebelum/sesudah, hash, lokasi preimage |
| Diagnostic | Kode, pesan asli, penjelasan, bukti, tindakan tersedia |
| TextureJob | Sumber, peran, profil, alpha, ukuran, output, hasil verifikasi |
| Recipe | Nama, versi, filter target, tindakan deklaratif, persyaratan |

Identitas KUID yang memiliki revisi tidak diratakan menjadi teks nama aset. Pemilihan versi aktual harus mengikuti hasil resolusi profil dan mempertahankan identitas yang diminta dalam catatan perubahan.

## 25. Kebutuhan nonfungsional

Angka berikut merupakan target pengujian, belum hasil benchmark. Mesin acuan awal: Windows x64, SSD, RAM 16 GB; fixture pustaka 50.000 catatan lokal. Waktu respons layanan Trainz dicatat terpisah dari waktu aplikasi.

| Area | Target penerimaan |
| --- | --- |
| Pencarian cache | p95 ≤ 300 ms setelah indeks siap |
| Respons UI | Operasi berat tidak memblokir interaksi; progres muncul ≤ 1 detik |
| Pembukaan pustaka cache | Hasil awal terlihat ≤ 3 detik pada mesin acuan |
| Scan pertama | Bertahap, dapat dihentikan pada batas aman, menampilkan kemajuan yang diketahui |
| Integritas | Semua transaksi tulis memiliki snapshot/jurnal yang terverifikasi |
| Offline | Editor folder, cache, konversi gambar, dan riwayat tetap berfungsi |
| Aksesibilitas | Navigasi keyboard, fokus jelas, status memakai teks selain warna, skala layar 100–200% |
| Bahasa | Indonesia sebagai bahasa awal; struktur teks siap diterjemahkan |
| Privasi | Aset dan log lokal; pengiriman laporan hanya tindakan pengguna, path pribadi dapat disamarkan |
| Keandalan | Crash/timeout memiliki rekonsiliasi; tidak ada pesan sukses hanya karena proses keluar |

Memori untuk konversi dibatasi dengan antrean; aplikasi tidak mendecode semua tekstur besar sekaligus. Format input memiliki batas ukuran/dimensi yang wajar dan kegagalan decoder tidak boleh merusak antrean lainnya.

## 26. Ukuran keberhasilan produk

Target ditetapkan untuk uji pengguna, kemudian disesuaikan setelah baseline manual diukur:

- ≥ 90% peserta pemula menyelesaikan tugas penggantian yang didukung tanpa membuka config. Ukur terpisah untuk hornsound, enginesound, tracksound, dan bogie; keberhasilan satu jenis tidak menutupi kegagalan jenis lain.
- Waktu interaksi aktif median untuk penggantian sederhana turun ≥ 60% dibanding alur manual pada aset dan mesin yang sama; waktu tunggu Trainz dilaporkan terpisah.
- Pengguna dapat menjelaskan komponen mana yang berubah sebelum menekan Selesai.
- Seluruh skenario kegagalan yang tercantum dalam matriks transaksi berakhir dengan keadaan yang bisa diidentifikasi dan jalur pemulihan.
- Tidak ada overwrite di luar cakupan yang dipilih pada corpus penerimaan.
- Hasil konversi tekstur lulus pemeriksaan kanal dan metadata pada seluruh fixture format yang diklaim didukung.
- Pada tugas P0 yang didukung, pengguna menyelesaikan edit lewat kontrol UI tanpa menyalin KUID dependency, membuka folder editing, atau menekan submit secara manual di aplikasi lain.
- Pengguna dapat menemukan tugas yang diinginkan dari halaman Mau edit apa? tanpa mengetahui nama utilitas lama atau nama tag config.

Jumlah fitur bukan ukuran utama. Produk dinilai dari berkurangnya pekerjaan manual dan kejelasan hasil.

## 27. Matriks penerimaan dan pengujian

| ID | Skenario | Hasil yang wajib terlihat |
| --- | --- | --- |
| AC-01 | Ganti hornsound pada aset terpilih | Hanya penggunaan yang dipilih dan dependency terkait berubah |
| AC-02 | KUID lama dipakai peran lain atau kunci script | Referensi lain dan nama simbolik tetap terjaga |
| AC-03 | Ada beberapa aturan tracksound | Hanya target aturan terpilih berubah; filter dan prioritas terjaga |
| AC-04 | Kandidat enginesound faulty atau jenisnya salah | Tidak menjadi pilihan standar; alasan terlihat |
| AC-05 | Config memiliki komentar/tag tak dikenal | Isi tersebut dipertahankan |
| AC-06 | Config ambigu/tidak valid | Otomasi tidak menulis patch spekulatif |
| AC-07 | Missing dependency bertingkat | Rantai penyebab dan target tindakan benar |
| AC-08 | Dependency terpasang tetapi faulty | Tidak ditampilkan sebagai download hilang |
| AC-09 | Aset sudah open for edit | Pekerjaan pengguna tidak diambil alih |
| AC-10 | File berubah sesudah preview rencana | Penerapan berhenti karena konflik |
| AC-11 | Commit timeout setelah perubahan diterima | Rekonsiliasi, tanpa commit ulang buta |
| AC-12 | Aplikasi mati sesudah penulisan | Jurnal menemukan pekerjaan dan menawarkan pemulihan |
| AC-13 | Undo setelah commit berhasil | Preimage dipulihkan dan hasil diverifikasi |
| AC-14 | Undo setelah edit eksternal | Konflik terlihat; edit eksternal tidak ditimpa |
| AC-15 | Batch gagal di aset kelima | Hasil tiap aset jelas, empat keberhasilan tetap tercatat |
| AC-16 | PNG alpha → TGA | Warna, alpha, orientasi benar saat dibaca ulang |
| AC-17 | Parameter map dengan alpha metallic | Alpha tidak dibuang sebagai transparansi tak terpakai |
| AC-18 | Dua sumber menghasilkan nama sama | Benturan diselesaikan sebelum penerapan |
| AC-19 | `.texture` dengan format tak dikenal | Sumber tidak diubah dan dukungan tidak dipalsukan |
| AC-20 | Tracksound pada aset bersama | Pemilik/cakupan terlihat sebelum perubahan |
| AC-21 | Dua instalasi memiliki KUID sama | Semua tindakan memakai profil yang dipilih |
| AC-22 | Ruang disk habis saat snapshot | Tidak ada penulisan aset |
| AC-23 | Validasi lolos tetapi suara/model tidak sesuai | Status validasi dan uji perilaku/visual dibedakan |
| AC-24 | Repair mengurangi sebagian error | Dilaporkan perbaikan sebagian; bukan sehat |
| AC-25 | Ekspor folder tanpa Trainz | Dilaporkan tersimpan, bukan sudah terpasang |
| AC-26 | Ganti satu slot bogie pada kendaraan dengan slot tambahan | Slot lain dan orientasinya tetap utuh |
| AC-27 | Ganti mesh library dengan path tidak tersedia | Penerapan ditolak atau meminta pemilihan path yang benar |
| AC-28 | kuid-table memiliki nama yang digunakan script | Nama tidak diubah, entri tidak dihapus tanpa bukti |
| AC-29 | Mengganti WAV pada soundscript | Transaksi file dipakai; tidak mengarang dependency KUID |
| AC-30 | Produk awal tidak cocok daftar izin queue | Konflik ditunjukkan sebelum penerapan |
| AC-31 | Ganti texture-group dengan kontrak script tak diketahui | Otomasi dibatasi, kebutuhan manual dijelaskan |
| AC-32 | Dependency route hilang | Diagnosis tersedia; ubah kuid-table tidak dilaporkan sebagai penggantian instance |
| AC-33 | Ubah hornsound dan deskripsi dari modul berbeda | Satu draft memuat keduanya tanpa saling menimpa |
| AC-34 | Pengguna mencari fungsi dari sembilan utilitas | Pemetaan modul dan status dukungan dapat ditemukan dari Workshop |
| AC-35 | Pengguna mencari mod berdasarkan nama atau KUID | Keduanya mencapai pusat edit aset dengan konteks yang benar |
| AC-36 | Edit sederhana pada profil P0 | Pilih aset → tujuan → pilihan → Selesai; tidak wajib membuka config/folder editing |
| AC-37 | Berpindah modul lalu kembali | Aset aktif dan perubahan draft tetap ada |
| AC-38 | Pemeriksaan keluaran desain AI | UI-01 sampai UI-25 memiliki visual; alur utama dapat ditelusuri; state kegagalan utama tersedia |
| AC-39 | Memeriksa mockup pada dua ukuran layar | Teks dan tindakan utama terbaca; panel tidak menutupi kontrol atau memotong tombol |

Metode pengujian: corpus config berizin/sintetis, fixture piksel yang diketahui, simulator adapter untuk kegagalan, lalu integrasi nyata pada profil Trainz khusus uji. Fixture menyertakan spasi/non-ASCII pada path, perbedaan akhir baris, versi KUID, serta hasil perintah dengan urutan/status berbeda.

Pengujian integrasi harus dilakukan pada setiap build yang akan dicantumkan sebagai didukung. Lolos TRS19 tidak otomatis berarti seluruh TRS22 atau Trainz Plus didukung.

## 28. Tahapan pengembangan dan gerbang rilis

### Tahap desain — menggambar UI terlebih dahulu

Ini adalah pekerjaan pertama AI yang menerima PRD. Hasilnya: peta layar, wireframe UI-01 sampai UI-25, mockup rinci untuk pusat edit dan alur utama, spesifikasi kontrol, serta contoh keadaan kosong/gagal/berhasil. Rancang satu pengalaman plug-and-play yang konsisten sebelum membangun layar produksi dan integrasi aset.

Gerbang: visual benar-benar dapat dilihat, fungsi setiap modul mempunyai tempat, seluruh langkah alur utama terhubung, serta AC-38/39 terpenuhi. Tinjau desain terhadap tujuan kemudahan dan perbaiki hambatan yang ditemukan. Kode prototipe visual boleh dipakai untuk menyajikan desain; itu tidak menjadi alasan mengerjakan backend, codec, atau operasi tulis Trainz pada tahap ini. Keluaran desain diserahkan terlebih dahulu; implementasi aplikasi merupakan tahap berikutnya.

### Tahap 0 — membuktikan integrasi

Hasil: adapter baca/tulis pada profil uji, corpus keluaran nyata, bukti submit/validasi, dan uji pemulihan. Uji transaksi pertama memakai aset uji yang dapat dibuang. Gerbang: siklus baca → ubah → submit → periksa → pulihkan dapat diulang, termasuk timeout dan crash.

### Tahap 1 — inti yang memudahkan

Hasil: onboarding, pustaka, dependency/resource inspector, mesin modifikasi umum, profil hornsound/enginesound/tracksound aturan yang ada/bogie, preview audio dasar untuk berkas yang dapat dibaca, diagnosis dependency, draft/diff, transaksi, riwayat, editor terbatas, serta gambar → TGA/PNG + metadata. Gerbang: seluruh profil P0 diuji secara setara, nama simbolik terjaga, dan tugas utama selesai tanpa pengetikan KUID.

### Tahap 2 — pekerjaan harian lengkap

Hasil: authoring/diagnosis konflik tracksound, interior/pantograph/enginespec, produk/queues, pergantian referensi mesh library, inspeksi texture-group/script, soundscript lokal, editor penuh, preview set audio, batch/preset, tambahan repair rule, serta perakitan PBR. Gerbang: dampak bersama, batch parsial, dan kanal tekstur lulus uji; pilot pengguna menunjukkan pengurangan langkah.

### Tahap 3 — kemampuan format dan 3D

Hasil bersyarat: codec `.texture`, renderer, pembacaan animasi, konversi mesh legacy, attachment, geometri bayangan. Setiap format memiliki gerbang sendiri. Jika pembaca/penulis format belum dapat dibuktikan, rilis inti tetap berjalan tanpa menjanjikan fitur itu.

Jadwal kalender ditentukan setelah Tahap 0 mengukur kesulitan integrasi. Pekerjaan format biner tidak diberi estimasi pasti sebelum sampel dan spesifikasinya diperiksa.

## 29. Backlog awal

Semua profil peran bergantung pada mesin umum. Tidak ada ketergantungan implementasi hornsound atau tracksound pada wizard bogie.

| ID | Pekerjaan | Prioritas | Ketergantungan | Selesai ketika |
| --- | --- | --- | --- | --- |
| RN-000 | Peta layar, wireframe, mockup dan alur plug-and-play | Pertama / P0 | PRD | AC-38/39 lulus; visual diserahkan sebelum implementasi aplikasi |
| RN-001 | Profil instalasi dan pemeriksaan kemampuan | P0 | Tidak ada | CONNECT-01 lulus |
| RN-002 | Adapter dan normalisasi hasil | P0 | RN-001 | Fixture build dikenali; hasil asing aman |
| RN-003 | Indeks pustaka dan pencarian | P0 | RN-002 | LIB terpenuhi dan benchmark dicatat |
| RN-004 | Parser, referensi, dan patch terarah | P0 | Corpus uji | AC-05/06/28 lulus |
| RN-005 | Transaksi, snapshot, jurnal, undo | P0 | RN-002/004 | AC-09 sampai 14, 22 lulus |
| RN-006 | Mesin modifikasi umum + Role Registry | P0 | RN-003/004/005 | Field, aturan, slot memakai rencana yang sama |
| RN-007 | Profil hornsound dan enginesound | P0 | RN-006 | AC-01/04/33 lulus |
| RN-008 | Dependency/resource graph dan diagnosis | P0 | RN-002/003/004 | AC-02/07/08/28/32 lulus |
| RN-009 | Workshop gambar dan metadata dasar | P0 | RN-005, codec gambar | AC-16/18/25 lulus |
| RN-010 | Editor lengkap dan resep | P1 | RN-004/005/006 | Teks/form/diff dan resep teruji |
| RN-011 | Profil tracksound untuk aturan yang ada | P0 | RN-006/008 | AC-03/20 lulus |
| RN-012 | Batch dan preset | P1 | RN-005/006 | AC-15 dan pengulangan resep lulus |
| RN-013 | Audio A/B dasar | P0 | Akses sumber audio | Preview/stop dan format gagal teruji |
| RN-014 | PBR channel packer | P1 | RN-009 | AC-17 dan fixture seluruh kanal lulus |
| RN-015 | Spike codec .texture | P2 | Sampel/format/lisensi | Matriks varian dan golden output tersedia |
| RN-016 | Spike viewer dan animasi | P2 | Pembaca format | VIEW-AC pada format terbatas lulus |
| RN-017 | Mesh conversion/attachment/shadow | P2 | RN-016, penulis/ekspor | Struktur, visual, dan round-trip tervalidasi |
| RN-018 | Profil bogie | P0 | RN-006 | AC-26 lulus |
| RN-019 | Interior, pantograph, enginespec | P1 | RN-006/008 | Kontrak peran dan uji fungsi terpisah |
| RN-020 | Queues dan produk | P1 | RN-006/008 | AC-30 lulus |
| RN-021 | Mesh library, texture-group, script | P1 | RN-006/008 | AC-27/28/31 lulus sesuai tingkat dukungan |
| RN-022 | Soundscript dan penyuntingan file audio | P1 | RN-005/013 | AC-29 lulus |
| RN-023 | Authoring tracksound dan preview set audio | P1 | RN-011/013 | Kondisi/pencocokan dan batas preview jelas |
| RN-024 | Peta sembilan utilitas pada Workshop | P0 | Navigasi workshop | AC-34 lulus; fitur riset memiliki label jelas |
| RN-025 | Pusat edit Mau edit apa? dan konteks lintas modul | P0 | RN-000/003/006 | AC-35/36/37 lulus |

Semua pekerjaan UI produksi mengikuti hasil RN-000. Urutan riset teknis setelah tahap desain mengikuti ketergantungan modul; fitur berat tidak menunda penyerahan visual awal.

## 30. Risiko dan keputusan yang masih terbuka

| Hal yang belum pasti | Dampak | Cara menyelesaikan |
| --- | --- | --- |
| Variasi output Trainz per build | Status/submit salah dibaca | Kumpulkan fixture dan adapter capability |
| Akses konten tertentu | Tidak semua aset dapat diedit/di-preview | Periksa kemampuan nyata dan jelaskan batas |
| Format mesh/tekstur biner | Viewer/converter tidak langsung tersedia | Riset codec terpisah; rilis per format |
| Script custom memilih dependency | Penggantian tag belum tentu efektif | Diagnosis script-aware hanya untuk aturan teruji |
| Metadata fungsi/fisik dependency tidak lengkap | Target valid tetapi suara, bentuk, atau perilakunya tidak sesuai | Status belum diperiksa, uji sesuai peran, anotasi sumber |
| Jalur backup/importer berbeda | Undo tidak lengkap | Uji pemulihan keluaran importer pada profil uji |
| Nama dan distribusi produk | Nama kerja belum final | Pemeriksaan merek dan keputusan lisensi sebelum publikasi |

Default rancangan: bahasa Indonesia, Windows x64, penggunaan lokal, implementasi mandiri, tanpa akun wajib, tanpa layanan cloud wajib, tanpa dependency PEVSoft. Pilihan lisensi produk dan model distribusi komersial belum diputuskan; tidak menghalangi prototipe lokal.

## 31. Definisi selesai untuk rilis inti

- Alur utama dapat digunakan orang yang belum memahami config Trainz; tugas suara, dependency, tekstur, dan komponen visual dinilai terpisah.
- Untuk alur yang didukung, pengguna memilih mod/KUID dan mengedit melalui UI; pengelolaan file, backup, dan submit dijalankan aplikasi.
- Setiap fitur rilis memiliki UI yang relevan, state yang jelas, dan konteks draft yang terjaga.
- Modul P0 memenuhi kriteria penerimaannya dan memiliki bukti uji.
- Daftar build yang didukung sesuai hasil pengujian nyata.
- Setiap perubahan terpasang memiliki rencana, snapshot, hasil validasi, dan catatan transaksi.
- Status berhasil tidak digunakan untuk pekerjaan yang belum diketahui hasilnya.
- Pengguna dapat membedakan sehat, perbaikan sebagian, perlu uji visual, dan gagal.
- Sumber gambar/aset di luar cakupan perubahan tetap utuh.
- Packaging memakai komponen yang boleh didistribusikan dan menyertakan atribusi yang diperlukan.
- Panduan singkat mencakup sambungkan, modifikasi, tekstur, perbaikan, dan undo.

## 32. Catatan sumber dan status pengetahuan

Semua spesifikasi RailNexus merupakan usulan produk. Fakta format/integrasi yang menjadi dasar keputusan telah dirujuk pada bagian terkait. Dokumentasi publik dapat tertinggal dari build aktual; pemeriksaan runtime dan pengujian tetap menjadi gerbang dukungan.

Sumber utama yang dipakai: dokumentasi N3V/Trainz untuk TrainzUtil, KIND Traincar, KIND Bogey, track-sound, Texture file, m.pbrmetal, dan FBX; Microsoft untuk siklus dukungan .NET; WIPO untuk prinsip umum hak cipta. Kesembilan utilitas yang disebut pengguna dipetakan pada bagian 19 beserta tautan sumber. Riset ulang dependency menggunakan TrainzBaseSpec, mesh-table, Queues, Texture-group, dan Script Include Directive; alur RailNexus tetap merupakan rancangan baru.

**Hasil yang dituju:** RailNexus menjadi tempat pengguna mengatakan perubahan apa yang diinginkan, melihat dampaknya, dan menyelesaikannya dengan sedikit langkah—dengan mekanisme teknis yang dapat diperiksa dan dipulihkan.

## 33. Instruksi untuk AI pelaksana — gambar UI terlebih dahulu

Bagian ini merupakan brief tugas awal untuk AI yang menerima PRD. Tugas awalnya adalah merancang dan memperlihatkan UI RailNexus. Detail teknis pada bagian lain menjadi batas dan konteks desain; implementasi aplikasi dilakukan sesudah keluaran desain tersedia.

### 33.1 Brief siap diberikan ke AI

```text
Kamu bertugas sebagai perancang produk dan UI/UX RailNexus. Baca PRD ini terlebih dahulu, terutama bagian 1, 3, 6, 9, 10, 19, dan 28.

TUJUAN UTAMA
RailNexus memudahkan seseorang mengedit mod/aset/KUID Trainz secara plug-and-play. Alurnya: pilih mod/KUID → pilih yang ingin diedit → atur lewat UI → Selesai. Pengguna tidak perlu memahami config, mencari folder editing, menyalin KUID dependency, atau mengurus submit manual untuk pekerjaan yang didukung.

TUGAS PERTAMAMU
Gambarkan UI aplikasinya terlebih dahulu. Buat visual yang benar-benar dapat dilihat, berupa mockup layar atau prototipe visual. Jangan hanya memberikan daftar fitur, deskripsi teks, atau diagram kotak tanpa menunjukkan isi layar. Jangan memulai backend, integrasi tulis Trainz, converter, atau implementasi aplikasi produksi pada tahap ini.

CAKUPAN
Sediakan UI yang jelas untuk memilih aset, pusat edit “Mau edit apa?”, ganti dependency, hornsound, enginesound, tracksound, komponen visual, fisika, muatan, library, config, perbaikan, tekstur, audio lokal, model, konversi mesh/bayangan, attachment, animasi, batch, proses/hasil, riwayat, dan pengaturan. Ikuti UI-01 sampai UI-25 pada bagian 6. Fitur dapat berupa tab atau panel dalam satu jendela.

Rancang berdasarkan kebutuhan umum pengguna. Berikan contoh suara, dependency, dan tekstur secara seimbang. Gunakan konteks jenis aset untuk menentukan pilihan yang muncul. Kesembilan kebutuhan utilitas pada bagian 19 harus bisa ditemukan dalam struktur produk, dengan implementasi dan identitas RailNexus sendiri.

URUTAN PEKERJAAN
1. Ringkas tujuan penggunaan dalam satu paragraf, lalu tentukan satu arah visual yang konsisten.
2. Buat peta navigasi dan alur dari memilih aset sampai hasil pekerjaan.
3. Gambarkan wireframe untuk seluruh UI-01 sampai UI-25; beri ID dan fungsi singkat.
4. Buat mockup rinci untuk paling sedikit: Pustaka, Pusat Edit, Pemilih Dependency, Hornsound, Enginesound, Tracksound, Perbaikan, Workshop Tekstur, serta Proses/Hasil. Ringkasan perubahan harus tampak pada layar edit yang relevan.
5. Gambarkan alur edit suara, edit aturan tracksound, perbaikan dependency, edit tekstur, mode manual, dan undo. Tunjukkan tombol asal dan layar tujuan.
6. Sertakan contoh keadaan memuat, kosong/tidak ada hasil, format tidak didukung, perubahan belum diterapkan, berhasil, gagal, dan konflik pemulihan.
7. Perlihatkan desain utama 1440 × 900 serta adaptasi layar kerja utama pada 1280 × 720.
8. Periksa desain terhadap tujuan plug-and-play, kemudian serahkan visual dan penjelasan singkat sebelum beralih ke tahap implementasi.

ATURAN UI
- Bahasa Indonesia yang mudah dipahami; istilah teknis dapat muncul sebagai keterangan/detail.
- Gunakan input sesuai tugas: daftar kandidat, dropdown, toggle, pemutar audio, preview gambar, atau viewport bila relevan.
- Nama aset dan tujuan pengguna lebih menonjol daripada KUID/tag config.
- Simpan konteks aset dan draft saat pengguna berpindah modul.
- Tampilkan ringkasan sebelum → sesudah di halaman kerja; pekerjaan sederhana tidak perlu wizard panjang atau konfirmasi berulang.
- Sediakan satu tindakan akhir “Terapkan & Selesai” untuk aset terpasang, dengan proses dan hasil yang terlihat.
- Jangan menyajikan UI seperti dashboard statistik yang tidak membantu pengguna mengedit mod.
- Bedakan fitur rilis inti dari fitur tahap lanjut pada anotasi desain. Mockup tidak menjadi bukti suatu codec/format sudah didukung.
- Gunakan data contoh yang diberi label; jangan mengaku telah membaca atau mengubah instalasi pengguna.
- Hindari satu lembar berisi puluhan screenshot kecil. Setiap layar harus cukup besar untuk membaca kontrol dan isi utamanya.

HASIL YANG HARUS DISERAHKAN
A. Peta layar dan jalur navigasi.
B. Wireframe seluruh UI-01 sampai UI-25.
C. Mockup rinci alur utama, dengan nomor layar yang konsisten.
D. Contoh state penting dan adaptasi ukuran layar.
E. Catatan singkat: fungsi kontrol utama, alur saat diklik, langkah teknis yang diurus aplikasi, dan bagian yang masih memerlukan implementasi/riset.

Jika alat yang tersedia tidak dapat menghasilkan atau menampilkan visual, jelaskan keterbatasannya dengan jujur. Jangan menyebut uraian teks sebagai mockup yang sudah digambar. Bila memungkinkan, gunakan prototipe visual yang bisa dibuka untuk memenuhi kebutuhan tampilan.
```

### 33.2 Pemeriksaan keluaran desain

- Ada visual nyata untuk setiap ID layar, meskipun fitur P1/P2 masih berupa wireframe.
- Ada mockup rinci untuk tugas umum dengan teks dan kontrol yang terbaca.
- Pengguna dapat menunjukkan cara mulai mengedit sebuah mod tanpa penjelasan teknis tambahan.
- Pemilih suara, aturan tracksound, dependency, dan tekstur memiliki kontrol yang sesuai tugasnya.
- Selesai memiliki tujuan dan state hasil yang jelas; undo serta kegagalan tidak dilupakan.
- Desain menampilkan satu aplikasi yang konsisten dan menjaga konteks aset lintas fitur.
- Hasil diperiksa terhadap bagian 6 dan tidak mengklaim integrasi Trainz telah berfungsi hanya karena mockup selesai.
