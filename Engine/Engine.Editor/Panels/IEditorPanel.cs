namespace Engine.Editor.Panels;

public interface IEditorPanel
{
    string Title { get; }

    bool IsOpen { get; set; }

    void Draw();
}

public abstract class EditorPanel : IEditorPanel
{
    public abstract string Title { get; }

    public bool IsOpen { get; set; } = true;

    public abstract void Draw();
}
