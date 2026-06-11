import Foundation
import UserNotifications

protocol GameNotificationSending {
    func showGameSelected(_ game: GameEntry, title: String)
}

struct NullGameNotificationService: GameNotificationSending {
    func showGameSelected(_ game: GameEntry, title: String) {}
}

final class MacGameNotificationService: GameNotificationSending {
    func showGameSelected(_ game: GameEntry, title: String) {
        let center = UNUserNotificationCenter.current()
        center.getNotificationSettings { settings in
            let isAllowed = settings.authorizationStatus == .authorized ||
                settings.authorizationStatus == .provisional
            guard isAllowed else { return }

            let content = UNMutableNotificationContent()
            content.title = title
            content.body = game.title

            if
                let coverURL = game.coverURL,
                coverURL.isFileURL,
                FileManager.default.fileExists(atPath: coverURL.path),
                let attachment = try? UNNotificationAttachment(identifier: "cover", url: coverURL)
            {
                content.attachments = [attachment]
            }

            let request = UNNotificationRequest(
                identifier: "steam-backlog-picker-\(UUID().uuidString)",
                content: content,
                trigger: nil
            )
            center.add(request)
        }
    }
}
