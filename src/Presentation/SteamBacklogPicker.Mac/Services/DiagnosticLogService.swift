import Foundation
import OSLog

protocol DiagnosticLogging {
    func info(_ message: String)
    func error(_ message: String)
}

struct NullDiagnosticLogger: DiagnosticLogging {
    func info(_ message: String) {}
    func error(_ message: String) {}
}

final class MacDiagnosticLogger: DiagnosticLogging {
    private let logger = Logger(subsystem: "com.steambacklogpicker.mac", category: "app")

    func info(_ message: String) {
        logger.info("\(message, privacy: .public)")
    }

    func error(_ message: String) {
        logger.error("\(message, privacy: .public)")
    }
}
