# Functional patterns: callbacks via name, accumulators,
# functions returning functions' results, pipeline pattern

# ── 1. Apply function by name pattern ──
fun double [x]
    return x * 2
end
fun square [x]
    return x * x
end
fun negate [x]
    return -x
end

fun apply_to_array [arr, n, func_name]
    result = {}
    loop i in 0..n
        if func_name == "double"
            result << double [arr{i}]
        elif func_name == "square"
            result << square [arr{i}]
        elif func_name == "negate"
            result << negate [arr{i}]
        end
    end
    return result
end

assert apply_to_array [{1, 2, 3, 4}, 4, "double"] == {2, 4, 6, 8}
assert apply_to_array [{1, 2, 3, 4}, 4, "square"] == {1, 4, 9, 16}
assert apply_to_array [{1, 2, 3, 4}, 4, "negate"] == {-1, -2, -3, -4}

# ── 2. Reduce pattern with operation name ──
fun reduce [arr, n, op, initial]
    acc = initial
    loop i in 0..n
        if op == "add"
            acc = acc + arr{i}
        elif op == "mul"
            acc = acc * arr{i}
        elif op == "max"
            if arr{i} > acc
                acc = arr{i}
            end
        elif op == "min"
            if arr{i} < acc
                acc = arr{i}
            end
        end
    end
    return acc
end

assert reduce [{1, 2, 3, 4, 5}, 5, "add", 0] == 15
assert reduce [{1, 2, 3, 4, 5}, 5, "mul", 1] == 120
assert reduce [{3, 7, 1, 9, 2}, 5, "max", 0] == 9
assert reduce [{3, 7, 1, 9, 2}, 5, "min", 999] == 1

# ── 3. Pipeline: chain of transformations ──
fun pipeline [value]
    step1 = value * 2
    step2 = step1 + 10
    step3 = step2 * step2
    return step3
end
assert pipeline [5] == 400
assert pipeline [0] == 100
assert pipeline [10] == 900

# ── 4. Recursive accumulator (tail-call style) ──
fun sum_tail [arr, n, i, acc]
    if i >= n
        return acc
    end
    return sum_tail [arr, n, i + 1, acc + arr{i}]
end
assert sum_tail [{10, 20, 30}, 3, 0, 0] == 60
assert sum_tail [{}, 0, 0, 0] == 0
assert sum_tail [{5}, 1, 0, 0] == 5

# ── 5. Function composing results of other functions ──
fun add [a, b]
    return a + b
end
fun mul [a, b]
    return a * b
end

fun compute [x, y]
    s = add [x, y]
    p = mul [x, y]
    d = add [s, p]
    return d
end
assert compute [3, 4] == 19
assert compute [0, 5] == 5
assert compute [2, 2] == 8

# ── 6. Recursive filter ──
fun filter_positive [arr, n, i]
    if i >= n
        return {}
    end
    rest = filter_positive [arr, n, i + 1]
    if arr{i} > 0
        result = {arr{i}}
        result = result + rest
        return result
    end
    return rest
end
assert filter_positive [{-1, 2, -3, 4, -5, 6}, 6, 0] == {2, 4, 6}
assert filter_positive [{-1, -2, -3}, 3, 0] == {}
assert filter_positive [{1, 2, 3}, 3, 0] == {1, 2, 3}

# ── 7. Multi-return via array ──
fun divmod [a, b]
    return {floor [a / b], a % b}
end
r = divmod [17, 5]
assert r{0} == 3
assert r{1} == 2
r2 = divmod [100, 7]
assert r2{0} == 14
assert r2{1} == 2

# ── 8. Chained function calls ──
fun inc [x]
    return x + 1
end
fun dbl [x]
    return x * 2
end
assert dbl [inc [dbl [inc [0]]]] == 6
assert inc [inc [inc [inc [inc [0]]]]] == 5
assert dbl [dbl [dbl [1]]] == 8
