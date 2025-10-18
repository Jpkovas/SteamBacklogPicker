using System.Threading;
using System.Threading.Tasks;
using Domain;

namespace SteamBacklogPicker.UI.Services;

public interface IGameUserDataService
{
    Task<GameUserData> LoadAsync(GameEntry game, CancellationToken cancellationToken = default);

    Task<GameUserData> SaveAsync(uint appId, GameUserData userData, CancellationToken cancellationToken = default);
}
