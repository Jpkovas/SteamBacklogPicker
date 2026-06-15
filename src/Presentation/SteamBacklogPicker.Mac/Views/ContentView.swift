import AppKit
import SwiftUI

private enum LayoutMetrics {
    static let bodyHorizontalPadding: CGFloat = 24
    static let bodyVerticalPadding: CGFloat = 20
    static let contentMaxWidth: CGFloat = 1120
    static let sidebarWidth: CGFloat = 320
}

struct ContentView: View {
    @EnvironmentObject private var store: AppStore

    var body: some View {
        VStack(spacing: 0) {
            HeaderView()

            HStack(spacing: 24) {
                FilterSidebarView()
                    .frame(width: LayoutMetrics.sidebarWidth)

                GameDetailView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .frame(maxWidth: LayoutMetrics.contentMaxWidth, maxHeight: .infinity, alignment: .top)
            .padding(.horizontal, LayoutMetrics.bodyHorizontalPadding)
            .padding(.vertical, LayoutMetrics.bodyVerticalPadding)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
        .background(
            LinearGradient(
                colors: [
                    Color(nsColor: .windowBackgroundColor),
                    Color(nsColor: .controlBackgroundColor)
                ],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            )
        )
    }
}

private struct HeaderView: View {
    @EnvironmentObject private var store: AppStore

    var body: some View {
        HStack(spacing: 14) {
            ZStack {
                RoundedRectangle(cornerRadius: 11, style: .continuous)
                    .fill(LinearGradient(colors: [.cyan, .blue], startPoint: .topLeading, endPoint: .bottomTrailing))

                Circle()
                    .fill(.white)
                    .frame(width: 6.5, height: 6.5)
                    .offset(x: -10, y: -10)
                Circle()
                    .fill(.white)
                    .frame(width: 6.5, height: 6.5)
                Circle()
                    .fill(.white)
                    .frame(width: 6.5, height: 6.5)
                    .offset(x: 10, y: 10)
            }
            .frame(width: 42, height: 42)

            VStack(alignment: .leading, spacing: 2) {
                Text("Steam Backlog Picker")
                    .font(.system(size: 22, weight: .semibold))
                    .lineLimit(1)
                Text(store.text("Header_Tagline"))
                    .foregroundStyle(.secondary)
                    .font(.system(size: 13))
                    .lineLimit(1)
            }

            Spacer()

            Picker("", selection: $store.language) {
                Text("BR").tag(AppLanguage.portuguese)
                Text("US").tag(AppLanguage.english)
            }
            .pickerStyle(.segmented)
            .frame(width: 112)
            .help("\(store.text("Language_Portuguese")) / \(store.text("Language_English"))")
            .accessibilityIdentifier("LanguageSelector")
            .accessibilityLabel("\(store.text("Language_Portuguese")) / \(store.text("Language_English"))")
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .frame(maxWidth: LayoutMetrics.contentMaxWidth)
        .padding(.horizontal, LayoutMetrics.bodyHorizontalPadding)
        .padding(.vertical, 14)
        .frame(maxWidth: .infinity)
        .background(.regularMaterial)
        .overlay(alignment: .bottom) {
            Rectangle()
                .fill(.separator)
                .frame(height: 1)
        }
    }
}

private struct FilterSidebarView: View {
    @EnvironmentObject private var store: AppStore

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(store.text("Filters_Title"))
                .font(.system(size: 20, weight: .semibold))
                .padding(.bottom, 16)

            ScrollView {
                VStack(alignment: .leading, spacing: 10) {
                    Toggle(store.text("Filters_RequireInstalled"), isOn: $store.preferences.filters.requireInstalled)
                        .accessibilityIdentifier("Filters_RequireInstalled")
                    Toggle(store.text("Filters_ExcludeDeckUnsupported"), isOn: $store.preferences.filters.excludeDeckUnsupported)
                        .accessibilityIdentifier("Filters_ExcludeDeckUnsupported")
                    Toggle(store.text("Filters_RequireMacCompatible"), isOn: $store.preferences.filters.requireMacCompatible)
                        .accessibilityIdentifier("Filters_RequireMacCompatible")

                    SectionLabel(store.text("Filters_ContentTypesLabel"))
                    Toggle(store.text("Filters_IncludeGames"), isOn: store.bindingForCategory(.game))
                        .accessibilityIdentifier("Filters_IncludeGames")
                    Toggle(store.text("Filters_IncludeSoundtracks"), isOn: store.bindingForCategory(.soundtrack))
                        .accessibilityIdentifier("Filters_IncludeSoundtracks")
                    Toggle(store.text("Filters_IncludeSoftware"), isOn: store.bindingForCategory(.software))
                        .accessibilityIdentifier("Filters_IncludeSoftware")
                    Toggle(store.text("Filters_IncludeTools"), isOn: store.bindingForCategory(.tool))
                        .accessibilityIdentifier("Filters_IncludeTools")
                    Toggle(store.text("Filters_IncludeVideos"), isOn: store.bindingForCategory(.video))
                        .accessibilityIdentifier("Filters_IncludeVideos")
                    Toggle(store.text("Filters_IncludeOther"), isOn: store.bindingForCategory(.other))
                        .accessibilityIdentifier("Filters_IncludeOther")

                    SectionLabel(store.text("Filters_StorefrontsLabel"))
                    Toggle(store.text("Filters_IncludeSteam"), isOn: steamStorefrontBinding)
                        .accessibilityIdentifier("Filters_IncludeSteam")

                    SectionLabel(store.text("Filters_CollectionLabel"))
                    CollectionMenu()
                }
                .toggleStyle(.checkbox)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.trailing, 4)
                .padding(.bottom, 10)
            }

            Divider()
                .padding(.top, 14)
                .padding(.bottom, 12)

            Button {
                Task { await store.refreshLibrary() }
            } label: {
                Label(store.text("Filters_RefreshButton"), systemImage: "arrow.clockwise")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.bordered)
            .controlSize(.large)
            .disabled(!store.canRefresh)
            .accessibilityIdentifier("Filters_RefreshButton")
            .accessibilityLabel(store.text("Filters_RefreshButton_Automation"))

            Button {
                Task { await store.drawGame() }
            } label: {
                Label(store.text("Filters_DrawButton"), systemImage: "shuffle")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(!store.canDraw)
            .padding(.top, 10)
            .help(store.text("Filters_DrawButton_HelpText"))
            .accessibilityIdentifier("Filters_DrawButton")
            .accessibilityLabel(store.text("Filters_DrawButton_Automation"))

            HStack(alignment: .top, spacing: 10) {
                Circle()
                    .fill(.green)
                    .frame(width: 8, height: 8)
                    .padding(.top, 5)
                Text(store.statusMessage)
                    .font(.system(size: 13))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .stroke(.separator.opacity(0.6))
            }
            .padding(.top, 14)
            .accessibilityIdentifier("StatusMessage")
            .accessibilityLabel(store.text("Status_Title"))
            .accessibilityValue(store.statusMessage)
        }
        .padding(20)
        .cardSurface()
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("FiltersPanel")
        .accessibilityLabel(store.text("Filters_PanelAutomationName"))
    }

    private var steamStorefrontBinding: Binding<Bool> {
        Binding(
            get: {
                !store.preferences.filters.filterByStorefront || store.preferences.filters.includedStorefronts.contains(.steam)
            },
            set: { value in
                store.preferences.filters.filterByStorefront = true
                if value {
                    store.preferences.filters.includedStorefronts.insert(.steam)
                } else {
                    store.preferences.filters.includedStorefronts.remove(.steam)
                }
            }
        )
    }
}

private struct CollectionMenu: View {
    @EnvironmentObject private var store: AppStore

    var body: some View {
        let selection = store.selectedCollectionBinding()
        let currentSelection = selection.wrappedValue

        Menu {
            ForEach(store.collectionOptions, id: \.self) { option in
                Button {
                    selection.wrappedValue = option
                } label: {
                    if option == currentSelection {
                        Label(option, systemImage: "checkmark")
                    } else {
                        Text(option)
                    }
                }
            }
        } label: {
            HStack(spacing: 10) {
                Text(currentSelection)
                    .lineLimit(1)
                    .truncationMode(.tail)

                Spacer(minLength: 8)

                Image(systemName: "chevron.up.chevron.down")
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 14)
            .frame(maxWidth: .infinity, minHeight: 34, alignment: .leading)
            .contentShape(RoundedRectangle(cornerRadius: 17, style: .continuous))
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 17, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 17, style: .continuous)
                .stroke(.separator.opacity(0.35))
        }
        .help(store.text("Filters_SelectCollection_HelpText"))
        .accessibilityIdentifier("Filters_SelectCollection")
        .accessibilityLabel(store.text("Filters_SelectCollection"))
        .accessibilityValue(currentSelection)
    }
}

private struct SectionLabel: View {
    let title: String

    init(_ title: String) {
        self.title = title
    }

    var body: some View {
        Text(title)
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .padding(.top, 12)
    }
}

private struct GameDetailView: View {
    @EnvironmentObject private var store: AppStore

    var body: some View {
        GeometryReader { geometry in
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text(store.text("GameDetails_NextGameTitle"))
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundStyle(.secondary)
                        Text(store.selectedGame?.title ?? store.text("GameDetails_NoSelectionTitle"))
                            .font(.system(size: 34, weight: .bold))
                            .lineLimit(2)
                            .minimumScaleFactor(0.7)
                            .accessibilityIdentifier("GameDetails_SelectedTitle")
                            .accessibilityLabel(store.text("GameDetails_SelectedTitleAutomation"))
                    }

                    Spacer()

                    if store.selectedGame?.storefront == .steam {
                        Label(store.text("Storefront_Steam"), systemImage: "cloud")
                            .font(.system(size: 12, weight: .medium))
                            .padding(.horizontal, 12)
                            .padding(.vertical, 8)
                            .background(Color(nsColor: .controlBackgroundColor), in: Capsule())
                    }
                }

                GameArtworkView(
                    game: store.selectedGame,
                    isDrawing: store.isDrawing,
                    noSelectionTitle: store.text("GameDetails_NoSelectionTitle"),
                    noCoverTitle: store.text("GameDetails_NoCoverTitle"),
                    noCoverSubtitle: store.text("GameDetails_NoCoverSubtitle"),
                    drawPrompt: store.text("GameDetails_DrawPrompt"),
                    drawingText: store.text("Status_Drawing")
                )
                .frame(height: artworkHeight(for: geometry.size.height))
                .padding(.top, 18)

                HStack(alignment: .bottom, spacing: 16) {
                    VStack(alignment: .leading, spacing: 8) {
                        Text(store.selectedGame.map(store.installStateText) ?? store.text("GameDetails_InstallState_Unknown"))
                            .font(.system(size: 14, weight: .semibold))
                            .lineLimit(2)
                            .fixedSize(horizontal: false, vertical: true)
                            .layoutPriority(1)
                            .accessibilityIdentifier("GameDetails_InstallationStatus")
                            .accessibilityLabel(store.text("GameDetails_InstallationAutomation"))

                        TagCloud(tags: store.selectedGameTags)
                    }
                    .layoutPriority(1)
                    .frame(maxWidth: 520, alignment: .leading)

                    Spacer()

                    HStack(spacing: 10) {
                        Button(store.text("GameDetails_InstallButton")) {
                            store.installSelectedGame()
                        }
                        .controlSize(.large)
                        .frame(width: 104)
                        .disabled(!store.canInstallSelectedGame)
                        .accessibilityIdentifier("GameDetails_InstallButton")
                        .accessibilityLabel(store.text("GameDetails_InstallButton_Automation"))

                        if store.canLaunchSelectedGame {
                            Button {
                                store.openSelectedGame()
                            } label: {
                                Label(store.text("GameDetails_PlayButton"), systemImage: "play.fill")
                                    .frame(width: 106)
                            }
                            .buttonStyle(.borderedProminent)
                            .controlSize(.large)
                            .accessibilityIdentifier("GameDetails_PlayButton")
                            .accessibilityLabel(store.text("GameDetails_PlayButton_Automation"))
                        } else {
                            Button {
                                store.openSelectedGame()
                            } label: {
                                Label(store.text("GameDetails_PlayButton"), systemImage: "play.fill")
                                    .frame(width: 106)
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.large)
                            .disabled(true)
                            .accessibilityIdentifier("GameDetails_PlayButton")
                            .accessibilityLabel(store.text("GameDetails_PlayButton_Automation"))
                        }
                    }
                }
                .padding(.top, 18)
            }
        }
        .padding(20)
        .cardSurface()
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("GameDetailsPanel")
        .accessibilityLabel(store.text("GameDetails_PanelAutomationName"))
    }

    private func artworkHeight(for availableHeight: CGFloat) -> CGFloat {
        min(410, max(260, availableHeight - 176))
    }
}

private struct GameArtworkView: View {
    let game: GameEntry?
    let isDrawing: Bool
    let noSelectionTitle: String
    let noCoverTitle: String
    let noCoverSubtitle: String
    let drawPrompt: String
    let drawingText: String

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .fill(Color(nsColor: .textBackgroundColor).opacity(0.55))

            let artworkURLs = game?.artworkURLs ?? []
            if !artworkURLs.isEmpty {
                FallbackArtworkImage(urls: artworkURLs) {
                    placeholder
                }
                .id(game?.id ?? "empty")
            } else {
                placeholder
            }

            if isDrawing {
                Rectangle()
                    .fill(.black.opacity(0.76))
                VStack(spacing: 16) {
                    ProgressView()
                        .controlSize(.large)
                    Text(drawingText)
                        .foregroundStyle(.secondary)
                        .font(.system(size: 15, weight: .medium))
                }
                .accessibilityIdentifier("GameDetails_DrawingOverlay")
                .accessibilityLabel(drawingText)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 18, style: .continuous)
                .stroke(.separator.opacity(0.6))
        }
        .frame(maxWidth: .infinity)
    }

    private var placeholder: some View {
        VStack(spacing: 10) {
            Text(game == nil ? noSelectionTitle : noCoverTitle)
                .font(.system(size: 19, weight: .semibold))
                .foregroundStyle(.secondary)
            Text(game == nil ? drawPrompt : noCoverSubtitle)
                .font(.system(size: 14))
                .foregroundStyle(.tertiary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 360)
        }
        .padding(24)
    }
}

private struct FallbackArtworkImage<Placeholder: View>: View {
    let urls: [URL]
    @ViewBuilder let placeholder: () -> Placeholder
    @State private var currentIndex = 0

    var body: some View {
        if currentIndex < urls.count {
            ArtworkCandidateImage(url: urls[currentIndex], onFailure: showNextCandidate)
        } else {
            placeholder()
        }
    }

    private func showNextCandidate() {
        guard currentIndex < urls.count else { return }
        DispatchQueue.main.async {
            currentIndex += 1
        }
    }
}

private struct ArtworkCandidateImage: View {
    let url: URL
    let onFailure: () -> Void

    var body: some View {
        if url.isFileURL {
            if let image = NSImage(contentsOf: url) {
                Image(nsImage: image)
                    .resizable()
                    .scaledToFit()
                    .padding(2)
            } else {
                ProgressView()
                    .onAppear(perform: onFailure)
            }
        } else {
            AsyncImage(url: url) { phase in
                switch phase {
                case let .success(image):
                    image
                        .resizable()
                        .scaledToFit()
                        .padding(2)
                case .empty:
                    ProgressView()
                case .failure:
                    ProgressView()
                        .onAppear(perform: onFailure)
                @unknown default:
                    ProgressView()
                        .onAppear(perform: onFailure)
                }
            }
        }
    }
}

private struct TagCloud: View {
    let tags: [String]

    var body: some View {
        FlowLayout(spacing: 8) {
            ForEach(visibleTags, id: \.self) { tag in
                Text(tag)
                    .font(.system(size: 12, weight: .medium))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(Color(nsColor: .controlBackgroundColor), in: Capsule())
            }

            if hiddenCount > 0 {
                Text("+\(hiddenCount)")
                    .font(.system(size: 12, weight: .medium))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                    .background(Color(nsColor: .controlBackgroundColor), in: Capsule())
            }
        }
        .frame(maxHeight: 68, alignment: .topLeading)
        .clipped()
    }

    private var visibleTags: [String] {
        Array(tags.prefix(4))
    }

    private var hiddenCount: Int {
        max(0, tags.count - visibleTags.count)
    }
}

private struct CardSurfaceModifier: ViewModifier {
    func body(content: Content) -> some View {
        content
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .stroke(.separator.opacity(0.65))
            }
    }
}

private extension View {
    func cardSurface() -> some View {
        modifier(CardSurfaceModifier())
    }
}
