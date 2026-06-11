import Foundation

protocol AppUpdateChecking {
    func checkForUpdates() async
}

struct NoOpMacAppUpdateService: AppUpdateChecking {
    func checkForUpdates() async {}
}
