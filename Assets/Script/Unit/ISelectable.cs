public interface ISelectable
{
    bool IsSelected { get; }
    bool CanBeSelected { get; }

    void SetSelected(bool selected);
}
