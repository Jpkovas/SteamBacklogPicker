import AppKit
import SwiftUI

private enum WindowMetrics {
    static let initialSize = NSSize(width: 900, height: 650)
    static let minSize = NSSize(width: 900, height: 650)
    static let maxSize = NSSize(width: 1168, height: 830)
}

@main
enum SteamBacklogPickerMacMain {
    @MainActor
    private static let delegate = SteamBacklogPickerMacApp()

    @MainActor
    static func main() {
        let application = NSApplication.shared
        application.delegate = delegate
        application.setActivationPolicy(.regular)
        application.finishLaunching()
        application.run()
    }
}

@MainActor
private final class SteamBacklogPickerMacApp: NSObject, NSApplicationDelegate, NSMenuItemValidation, NSWindowDelegate {
    private var store: AppStore!
    private var window: NSWindow?

    func applicationWillFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        configureMenu()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        store = AppStore()
        store.languageDidChange = { [weak self] in
            self?.configureMenu()
        }
        configureMenu()
        showMainWindow()
        Task { await store.checkForUpdates() }
        Task { await store.refreshLibrary() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            showMainWindow()
        }
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        if menuItem.action == #selector(refreshLibrary(_:)) {
            return store?.canRefresh == true
        }
        if menuItem.action == #selector(drawGame(_:)) {
            return store?.canDraw == true
        }
        return true
    }

    private func showMainWindow() {
        if let window {
            window.makeKeyAndOrderFront(nil)
            window.orderFrontRegardless()
            NSApp.activate(ignoringOtherApps: true)
            NSRunningApplication.current.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
            return
        }

        let contentView = ContentView()
            .environmentObject(store)
            .frame(minWidth: WindowMetrics.minSize.width, minHeight: WindowMetrics.minSize.height)
        let window = NSWindow(
            contentRect: NSRect(origin: .zero, size: WindowMetrics.initialSize),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )

        window.title = "Steam Backlog Picker"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = false
        window.isReleasedWhenClosed = false
        window.minSize = WindowMetrics.minSize
        window.maxSize = WindowMetrics.maxSize
        window.collectionBehavior = [.fullScreenNone]
        window.standardWindowButton(.zoomButton)?.isEnabled = false
        window.delegate = self
        window.center()
        window.contentView = NSHostingView(rootView: contentView)
        clampWindowToAllowedSize(window)

        self.window = window
        window.makeKeyAndOrderFront(nil)
        window.orderFrontRegardless()
        NSApp.activate(ignoringOtherApps: true)
        NSRunningApplication.current.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
    }

    func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize {
        clampedWindowSize(frameSize)
    }

    func windowDidResize(_ notification: Notification) {
        guard let window = notification.object as? NSWindow else {
            return
        }

        clampWindowToAllowedSize(window)
    }

    func windowShouldZoom(_ window: NSWindow, toFrame newFrame: NSRect) -> Bool {
        clampWindowToAllowedSize(window)
        return false
    }

    private func clampedWindowSize(_ size: NSSize) -> NSSize {
        NSSize(
            width: min(max(size.width, WindowMetrics.minSize.width), WindowMetrics.maxSize.width),
            height: min(max(size.height, WindowMetrics.minSize.height), WindowMetrics.maxSize.height)
        )
    }

    private func clampWindowToAllowedSize(_ window: NSWindow) {
        let currentFrame = window.frame
        let clampedSize = clampedWindowSize(currentFrame.size)

        guard currentFrame.size != clampedSize else {
            return
        }

        var clampedFrame = currentFrame
        clampedFrame.size = clampedSize
        clampedFrame.origin.y = currentFrame.maxY - clampedSize.height
        window.setFrame(clampedFrame, display: true)
    }

    private func configureMenu() {
        let mainMenu = NSMenu()

        let appMenuItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(
            NSMenuItem(
                title: menuText("Menu_Quit"),
                action: #selector(NSApplication.terminate(_:)),
                keyEquivalent: "q"
            )
        )
        appMenuItem.submenu = appMenu
        mainMenu.addItem(appMenuItem)

        let fileMenuItem = NSMenuItem()
        let fileMenu = NSMenu(title: menuText("Menu_File"))
        let refreshItem = NSMenuItem(
            title: menuText("Filters_RefreshButton"),
            action: #selector(refreshLibrary(_:)),
            keyEquivalent: "r"
        )
        refreshItem.target = self
        fileMenu.addItem(refreshItem)

        let drawItem = NSMenuItem(
            title: menuText("Filters_DrawButton"),
            action: #selector(drawGame(_:)),
            keyEquivalent: "d"
        )
        drawItem.target = self
        fileMenu.addItem(drawItem)
        fileMenuItem.submenu = fileMenu
        mainMenu.addItem(fileMenuItem)

        NSApp.mainMenu = mainMenu
    }

    private func menuText(_ key: String) -> String {
        store?.text(key) ?? Localization.text(key, language: AppLanguage.preferred())
    }

    @objc private func refreshLibrary(_ sender: Any?) {
        Task { await store?.refreshLibrary() }
    }

    @objc private func drawGame(_ sender: Any?) {
        Task { await store?.drawGame() }
    }
}
