namespace RailNexus.App.Core.ViewModels;

/// <summary>
/// Workshop: peta sembilan utilitas dan status dukungan setiap modul (RN-024, AC-34), serta pintu
/// masuk modul yang dapat dipakai tanpa aset aktif (mode folder).
/// </summary>
public sealed class WorkshopViewModel : ObservableObject, IPage
{
    public WorkshopViewModel(Action openTextureFolder)
    {
        OpenTextureFolderCommand = new RelayCommand(openTextureFolder);
        Footer = new FooterAction("Buka Workshop Tekstur", OpenTextureFolderCommand,
            () => "Mode folder: konversi gambar dan metadata tanpa Trainz.");
    }

    public string Title => "Workshop";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }

    public IReadOnlyList<ModuleInfo> Modules => ModuleCatalog.All;
    public RelayCommand OpenTextureFolderCommand { get; }

    public string Intro =>
        "Semua modul berbagi aset aktif, draft, backup, dan riwayat yang sama. Kolom kebutuhan menyebut utilitas lama " +
        "yang fungsinya dipenuhi; RailNexus tidak memakai atau meniru kode aplikasi tersebut.";
}
