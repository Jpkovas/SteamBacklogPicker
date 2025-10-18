using Domain;

namespace Domain.Selection;

public interface ISelectionEngine
{
    SelectionPreferences GetPreferences();

    void UpdatePreferences(SelectionPreferences preferences);

    IReadOnlyList<SelectionHistoryEntry> GetHistory();

    void ClearHistory();

    GameUserData GetUserData(uint appId);

    void UpdateUserData(uint appId, GameUserData userData);

    GameEntry PickNext(IEnumerable<GameEntry> games);

    IReadOnlyList<GameEntry> FilterGames(IEnumerable<GameEntry> games);
}
