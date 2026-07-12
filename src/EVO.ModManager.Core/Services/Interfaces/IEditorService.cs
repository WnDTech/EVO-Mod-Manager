namespace EVO.ModManager.Core.Services.Interfaces;

public interface IEditorService
{
    bool IsEditorAvailable { get; }
    void LaunchEditor(string? modPath = null);
    string? EditorPath { get; }
}
