using System;
using System.Collections.Generic;
using System.Linq;
using Domain;
using EpicDiscovery;
using SteamClientAdapter;

namespace SteamBacklogPicker.UI.Services;

public sealed class EpicGameArtLocator : IGameArtLocator
{
    private static readonly string[] PreferredImageTypes =
    [
        "DieselGameBox",
        "DieselGameBoxWide",
        "DieselGameBoxTall",
        "VaultClosed",
        "OfferImageWide",
        "OfferImageTall",
        "HeroImage",
        "KeyImage",
        "Image"
    ];

    private readonly EpicMetadataCache metadataCache;
    private readonly IFileAccessor fileAccessor;

    public EpicGameArtLocator(EpicMetadataCache metadataCache, IFileAccessor fileAccessor)
    {
        this.metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
        this.fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));
    }

    public string? FindHeroImage(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (game.Id.Storefront != Storefront.EpicGamesStore)
        {
            return null;
        }

        var item = metadataCache.GetCatalogEntry(game.Id);
        if (item is null)
        {
            return null;
        }

        return SelectBestImage(item.KeyImages);
    }

    private string? SelectBestImage(IReadOnlyCollection<EpicKeyImage> keyImages)
    {
        if (keyImages is null || keyImages.Count == 0)
        {
            return null;
        }

        foreach (var type in PreferredImageTypes)
        {
            var candidate = keyImages.FirstOrDefault(image =>
                string.Equals(image.Type, type, StringComparison.OrdinalIgnoreCase));
            var resolved = ResolveImage(candidate);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        foreach (var image in keyImages)
        {
            var resolved = ResolveImage(image);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private string? ResolveImage(EpicKeyImage? image)
    {
        if (image is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(image.Path) && fileAccessor.FileExists(image.Path))
        {
            return image.Path;
        }

        if (!string.IsNullOrWhiteSpace(image.Uri))
        {
            return image.Uri;
        }

        return null;
    }
}
