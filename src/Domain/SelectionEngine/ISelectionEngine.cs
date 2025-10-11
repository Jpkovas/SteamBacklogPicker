using Domain;

namespace Domain.Selection;

public interface ISelectionEngine
{
    SelectionPreferences GetPreferences();

    void UpdatePreferences(SelectionPreferences preferences);

    IReadOnlyList<SelectionHistoryEntry> GetHistory();

    void ClearHistory();

    GameEntry PickNext(IEnumerable<GameEntry> games);
}
