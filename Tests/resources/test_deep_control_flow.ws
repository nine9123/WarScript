# ──────────────────────────────────────────────────────
# test_deep_control_flow.ws
# Deep control flow: complex nested loops, conditionals
# inside functions inside classes, break/next in nested
# contexts, while loops with state machines, early return
# ──────────────────────────────────────────────────────

# ── 1. Nested loops with break only affecting inner ──
result = {}
loop i in 0..4
    loop j in 0..10
        if j == 3
            break
        end
    end
    result << i
end
assert result == {0, 1, 2, 3}

# ── 2. Nested loops with next only affecting inner ──
result = {}
loop i in 0..3
    inner_sum = 0
    loop j in 0..10
        if j % 2 == 0
            next
        end
        inner_sum += j
    end
    result << inner_sum
end
assert result == {25, 25, 25}

# ── 3. FizzBuzz complete ──
result = {}
loop i in 1..16
    if i % 15 == 0
        result << "FizzBuzz"
    elif i % 3 == 0
        result << "Fizz"
    elif i % 5 == 0
        result << "Buzz"
    else
        result << i
    end
end
assert result{0} == 1
assert result{2} == "Fizz"
assert result{4} == "Buzz"
assert result{14} == "FizzBuzz"
assert result{6} == 7

# ── 4. While loop as state machine ──
state = "start"
steps = {}
loop state != "done"
    steps << state
    if state == "start"
        state = "processing"
    elif state == "processing"
        state = "validating"
    elif state == "validating"
        state = "complete"
    elif state == "complete"
        state = "done"
    end
end
assert steps == {"start", "processing", "validating", "complete"}

# ── 5. Early return from deeply nested structure ──
fun find_in_matrix [matrix, target, rows, cols]
    loop r in 0..rows
        row = matrix{r}
        loop c in 0..cols
            if row{c} == target
                return "found at " + r + "," + c
            end
        end
    end
    return "not found"
end

mat = {{1, 2, 3}, {4, 5, 6}, {7, 8, 9}}
assert find_in_matrix [mat, 5, 3, 3] == "found at 1,1"
assert find_in_matrix [mat, 9, 3, 3] == "found at 2,2"
assert find_in_matrix [mat, 1, 3, 3] == "found at 0,0"
assert find_in_matrix [mat, 10, 3, 3] == "not found"

# ── 6. Loop building conditional structure ──
fun categorize [values, n]
    low = {}
    mid = {}
    high = {}
    loop i in 0..n
        v = values{i}
        if v < 33
            low << v
        elif v < 66
            mid << v
        else
            high << v
        end
    end
    result = {}
    result << low
    result << mid
    result << high
    return result
end

categories = categorize [{10, 50, 80, 20, 90, 40, 70, 30, 60, 5}, 10]
low = categories{0}
mid = categories{1}
high = categories{2}
assert low == {10, 20, 30, 5}
assert mid == {50, 40, 60}
assert high == {80, 90, 70}

# ── 7. Break inside nested if inside loop ──
found_idx = -1
haystack = {"apple", "banana", "cherry", "date", "elderberry"}
loop i in 0..5
    if haystack{i} == "cherry"
        found_idx = i
        break
    end
end
assert found_idx == 2

# ── 8. While loop with multiple exit conditions ──
x = 0
y = 100
iterations = 0
loop x < 50 and y > 60
    x += 7
    y -= 5
    iterations += 1
end
assert iterations == 8
assert x == 56
assert y == 60

# ── 9. Nested if in loop with early next ──
result = {}
loop i in 0..10
    if i < 3
        next
    end
    if i > 7
        break
    end
    if i % 2 == 0
        result << "even:" + i
    else
        result << "odd:" + i
    end
end
assert result == {"odd:3", "even:4", "odd:5", "even:6", "odd:7"}

# ── 10. Class method with complex control flow ──
class Processor []
    fun process [data, n]
        result = {}
        loop i in 0..n
            val = data{i}
            if val < 0
                next
            end
            if val > 100
                break
            end
            if val % 2 == 0
                result << val / 2
            else
                result << val * 3 + 1
            end
        end
        return result
    end
end

p = new Processor
assert p :: process [{-5, 3, 4, -1, 7, 10, 200, 8}, 8] == {10, 2, 22, 5}

# ── 11. For loop with dynamic step ──
result = {}
loop i in 0..20 by 4
    result << i
end
assert result == {0, 4, 8, 12, 16}

# ── 12. Iterable loop with modification of external state ──
names = {"alice", "bob", "charlie"}
upper_names = {}
loop name in names
    upper_names << "Mr. " + name
end
assert upper_names == {"Mr. alice", "Mr. bob", "Mr. charlie"}

# ── 13. Deeply nested conditions ──
fun deep_classify [a, b, c]
    if a > 0
        if b > 0
            if c > 0
                return "+++"
            else
                return "++-"
            end
        else
            if c > 0
                return "+-+"
            else
                return "+--"
            end
        end
    else
        if b > 0
            return "-+?"
        else
            return "--?"
        end
    end
end
assert deep_classify [1, 1, 1] == "+++"
assert deep_classify [1, 1, -1] == "++-"
assert deep_classify [1, -1, 1] == "+-+"
assert deep_classify [1, -1, -1] == "+--"
assert deep_classify [-1, 1, 0] == "-+?"
assert deep_classify [-1, -1, 0] == "--?"
