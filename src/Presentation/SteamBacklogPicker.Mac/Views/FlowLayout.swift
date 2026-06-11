import SwiftUI

struct FlowLayout: Layout {
    var spacing: CGFloat = 8

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 0
        let rows = rows(for: subviews, width: width)
        return CGSize(width: width, height: rows.reduce(0) { $0 + $1.height } + CGFloat(max(0, rows.count - 1)) * spacing)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var origin = bounds.origin
        for row in rows(for: subviews, width: bounds.width) {
            var x = origin.x
            for item in row.items {
                item.subview.place(at: CGPoint(x: x, y: origin.y), proposal: ProposedViewSize(item.size))
                x += item.size.width + spacing
            }
            origin.y += row.height + spacing
        }
    }

    private func rows(for subviews: Subviews, width: CGFloat) -> [Row] {
        var rows: [Row] = []
        var current = Row()

        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if current.width + size.width > width, !current.items.isEmpty {
                rows.append(current)
                current = Row()
            }
            current.add(subview: subview, size: size, spacing: spacing)
        }

        if !current.items.isEmpty {
            rows.append(current)
        }

        return rows
    }

    private struct Row {
        var items: [Item] = []
        var width: CGFloat = 0
        var height: CGFloat = 0

        mutating func add(subview: LayoutSubview, size: CGSize, spacing: CGFloat) {
            items.append(Item(subview: subview, size: size))
            width += size.width + (items.count > 1 ? spacing : 0)
            height = max(height, size.height)
        }
    }

    private struct Item {
        var subview: LayoutSubview
        var size: CGSize
    }
}
