import Foundation

final class VDFNode {
    let name: String
    var value: String?
    var children: [String: VDFNode]

    init(name: String, value: String? = nil, children: [String: VDFNode] = [:]) {
        self.name = name
        self.value = value
        self.children = children
    }

    var allChildren: [VDFNode] {
        Array(children.values)
    }

    func child(_ name: String) -> VDFNode? {
        children.first { $0.key.caseInsensitiveCompare(name) == .orderedSame }?.value
    }

    func path(_ names: String...) -> VDFNode? {
        names.reduce(self as VDFNode?) { node, name in
            node?.child(name)
        }
    }
}

enum VDFParserError: Error, Equatable {
    case expectedString
    case unexpectedToken
    case unterminatedString
}

struct VDFParser {
    func parse(_ content: String) throws -> VDFNode {
        var tokenizer = VDFTokenizer(content)
        let root = VDFNode(name: "root")
        root.children = try parseObject(tokenizer: &tokenizer)
        return root
    }

    private func parseObject(tokenizer: inout VDFTokenizer) throws -> [String: VDFNode] {
        var result: [String: VDFNode] = [:]

        while let token = try tokenizer.peek() {
            if token == .closeBrace {
                _ = try tokenizer.read()
                break
            }

            guard case let .string(key) = try tokenizer.read() else {
                throw VDFParserError.expectedString
            }

            guard let valueToken = try tokenizer.peek() else {
                throw VDFParserError.unexpectedToken
            }

            switch valueToken {
            case .openBrace:
                _ = try tokenizer.read()
                result[key] = VDFNode(name: key, children: try parseObject(tokenizer: &tokenizer))
            case let .string(value):
                _ = try tokenizer.read()
                result[key] = VDFNode(name: key, value: value)
            case .closeBrace:
                throw VDFParserError.unexpectedToken
            }
        }

        return result
    }
}

private enum VDFToken: Equatable {
    case string(String)
    case openBrace
    case closeBrace
}

private struct VDFTokenizer {
    private let scalars: [Character]
    private var position = 0
    private var buffered: VDFToken?

    init(_ content: String) {
        scalars = Array(content)
    }

    mutating func peek() throws -> VDFToken? {
        if buffered == nil {
            buffered = try readNextToken()
        }
        return buffered
    }

    mutating func read() throws -> VDFToken? {
        if buffered == nil {
            buffered = try readNextToken()
        }
        defer { buffered = nil }
        return buffered
    }

    private mutating func readNextToken() throws -> VDFToken? {
        skipWhitespace()
        guard position < scalars.count else { return nil }

        let current = scalars[position]
        switch current {
        case "{":
            position += 1
            return .openBrace
        case "}":
            position += 1
            return .closeBrace
        case "\"":
            return .string(try readString())
        case "/" where peekNext("/") == true:
            skipComment()
            return try readNextToken()
        default:
            throw VDFParserError.unexpectedToken
        }
    }

    private mutating func skipWhitespace() {
        while position < scalars.count, scalars[position].isWhitespace {
            position += 1
        }
    }

    private func peekNext(_ expected: Character) -> Bool {
        position + 1 < scalars.count && scalars[position + 1] == expected
    }

    private mutating func skipComment() {
        position += 2
        while position < scalars.count, scalars[position] != "\n" {
            position += 1
        }
    }

    private mutating func readString() throws -> String {
        var result = ""
        position += 1

        while position < scalars.count {
            let current = scalars[position]
            position += 1

            if current == "\"" {
                return result
            }

            if current == "\\" {
                guard position < scalars.count else {
                    throw VDFParserError.unterminatedString
                }

                let escaped = scalars[position]
                position += 1
                switch escaped {
                case "n":
                    result.append("\n")
                case "r":
                    result.append("\r")
                case "t":
                    result.append("\t")
                case "\\", "\"":
                    result.append(escaped)
                default:
                    result.append(escaped)
                }
                continue
            }

            result.append(current)
        }

        throw VDFParserError.unterminatedString
    }
}
