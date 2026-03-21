# ──────────────────────────────────────────────────────
# test_deep_recursive_locals.ws
# Bug 6 regression tests: recursive functions with
# intermediate local variables. These were all broken
# before the fix because MemoryScope.Set walked up to
# the caller's scope and clobbered its locals.
# ──────────────────────────────────────────────────────

# ── 1. Tree depth with local variables ──
class TreeNode [val, left, right]
end

fun tree_depth [node]
    if node == null
        return 0
    end
    left_d = tree_depth [node :: left]
    right_d = tree_depth [node :: right]
    if left_d > right_d
        return left_d + 1
    end
    return right_d + 1
end

n4 = new TreeNode [4, null, null]
n5 = new TreeNode [5, null, null]
n6 = new TreeNode [6, null, null]
n2 = new TreeNode [2, n4, n5]
n3 = new TreeNode [3, null, n6]
root = new TreeNode [1, n2, n3]

assert tree_depth [root] == 3
assert tree_depth [n2] == 2
assert tree_depth [n3] == 2
assert tree_depth [n4] == 1
assert tree_depth [null] == 0

# ── 2. Fibonacci with local variables ──
fun fib [n]
    if n < 2
        return n
    end
    a = fib [n - 1]
    b = fib [n - 2]
    return a + b
end

assert fib [0] == 0
assert fib [1] == 1
assert fib [2] == 1
assert fib [5] == 5
assert fib [10] == 55

# ── 3. Merge two sorted arrays (merge step of merge sort) ──
fun merge [a, b, a_len, b_len]
    result = {}
    i = 0
    j = 0
    loop i < a_len and j < b_len
        if a{i} <= b{j}
            result << a{i}
            i += 1
        else
            result << b{j}
            j += 1
        end
    end
    loop i < a_len
        result << a{i}
        i += 1
    end
    loop j < b_len
        result << b{j}
        j += 1
    end
    return result
end

# ── 4. Merge sort — the algorithm that was impossible before ──
fun merge_sort [arr, lo, hi]
    if hi - lo <= 1
        return {arr{lo}}
    end
    mid = floor [lo + (hi - lo) / 2]
    # These locals were clobbered before the fix
    left_half = merge_sort [arr, lo, mid]
    right_half = merge_sort [arr, mid, hi]
    left_len = mid - lo
    right_len = hi - mid
    return merge [left_half, right_half, left_len, right_len]
end

data = {5, 3, 8, 1, 9, 2, 7, 4, 6, 0}
sorted = merge_sort [data, 0, 10]
assert sorted == {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}

small = {3, 1}
assert merge_sort [small, 0, 2] == {1, 3}

single = {42}
assert merge_sort [single, 0, 1] == {42}

# ── 5. Tree node count with local variables ──
fun count_nodes [node]
    if node == null
        return 0
    end
    left_count = count_nodes [node :: left]
    right_count = count_nodes [node :: right]
    return 1 + left_count + right_count
end

assert count_nodes [root] == 6
assert count_nodes [n2] == 3
assert count_nodes [n3] == 2
assert count_nodes [null] == 0

# ── 6. Tree sum with local variables ──
fun sum_tree [node]
    if node == null
        return 0
    end
    left_sum = sum_tree [node :: left]
    right_sum = sum_tree [node :: right]
    return node :: val + left_sum + right_sum
end

assert sum_tree [root] == 21
assert sum_tree [n2] == 11
assert sum_tree [n3] == 9

# ── 7. Power set size (2^n via recursion with locals) ──
fun power_of_two [n]
    if n == 0
        return 1
    end
    half = power_of_two [n - 1]
    return half + half
end

assert power_of_two [0] == 1
assert power_of_two [1] == 2
assert power_of_two [5] == 32
assert power_of_two [10] == 1024

# ── 8. Recursive flatten with locals ──
fun flatten_depth [arr, n, depth]
    result = {}
    loop i in 0..n
        item = arr{i}
        # For simplicity, check if item looks like a nested array
        # by trying to iterate — just flatten one level
        if depth > 0
            inner_len = 0
            loop x in item
                inner_len += 1
            end
            inner_flat = flatten_depth [item, inner_len, depth - 1]
            loop x in inner_flat
                result << x
            end
        else
            result << item
        end
    end
    return result
end

nested = {{1, 2}, {3, 4}, {5, 6}}
assert flatten_depth [nested, 3, 1] == {1, 2, 3, 4, 5, 6}

# ── 9. Recursive binary search with locals ──
fun bin_search [arr, target, lo, hi]
    if lo >= hi
        return -1
    end
    mid = floor [lo + (hi - lo) / 2]
    mid_val = arr{mid}
    if mid_val == target
        return mid
    end
    if target < mid_val
        result = bin_search [arr, target, lo, mid]
        return result
    end
    result = bin_search [arr, target, mid + 1, hi]
    return result
end

sorted_arr = {1, 3, 5, 7, 9, 11, 13, 15}
assert bin_search [sorted_arr, 7, 0, 8] == 3
assert bin_search [sorted_arr, 1, 0, 8] == 0
assert bin_search [sorted_arr, 15, 0, 8] == 7
assert bin_search [sorted_arr, 6, 0, 8] == -1

# ── 10. Standalone functions still see globals ──
counter = 0
fun recursive_count [n]
    if n <= 0
        return
    end
    counter += 1
    recursive_count [n - 1]
    return
end

recursive_count [5]
assert counter == 5

# ── 11. Recursive function calling another function ──
fun helper [x]
    return x * 2
end

fun recursive_with_call [n]
    if n <= 0
        return 0
    end
    val = helper [n]
    rest = recursive_with_call [n - 1]
    return val + rest
end

# 2*3 + 2*2 + 2*1 = 6+4+2 = 12
assert recursive_with_call [3] == 12
