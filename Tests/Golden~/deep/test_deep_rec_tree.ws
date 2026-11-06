# test_deep_rec_tree.ws — Recursive tree operations
# (builds tree step by step to reduce peak memory)

class Node [val, left, right]
end

fun count_nodes [node]
    if node == null
        return 0
    end
    return 1 + count_nodes [node :: left] + count_nodes [node :: right]
end

fun sum_nodes [node]
    if node == null
        return 0
    end
    return node :: val + sum_nodes [node :: left] + sum_nodes [node :: right]
end

fun tree_contains [node, target]
    if node == null
        return false
    end
    if node :: val == target
        return true
    end
    return tree_contains [node :: left, target] or tree_contains [node :: right, target]
end

# Build tree step by step instead of deeply nested constructor
n4 = new Node [4, null, null]
n5 = new Node [5, null, null]
n6 = new Node [6, null, null]
n2 = new Node [2, n4, n5]
n3 = new Node [3, null, n6]
tree = new Node [1, n2, n3]

assert count_nodes [tree] == 6
assert sum_nodes [tree] == 21
assert tree_contains [tree, 5]
assert tree_contains [tree, 1]
assert !tree_contains [tree, 7]
assert !tree_contains [tree, 0]
