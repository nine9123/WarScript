# ──────────────────────────────────────────────────────
# test_edge_cases.ws
# Covers: boundary conditions, empty constructs, single
#         element operations, zero/identity, deeply nested
#         structures, large iterations, type coercion
# ──────────────────────────────────────────────────────

# ── Zero and identity ──
assert 0 + 0 == 0
assert 0 * 100 == 0
assert 100 * 0 == 0
assert 0 / 1 == 0
assert 0 % 7 == 0

# ── Negative numbers ──
assert -1 + -1 == -2
assert -5 * -5 == 25
assert -10 / -2 == 5

# ── Very large loop ──
sum = 0
loop i in 0..1000
    sum += 1
end
assert sum == 1000

# ── Empty function body ──
fun noop
end
noop []

# ── Function with only return ──
fun returns_null
    return
end
assert returns_null [] == null

# ── Single character strings ──
s = "x"
assert s{0} == "x"
assert s == "x"
assert s * 3 == "xxx"

# ── Empty string ──
assert "" == ""
assert "" + "" == ""
assert "" * 10 == ""

# ── Boolean as values ──
assert true != 1
assert false != 0
assert "flag: " + true == "flag: True"
assert "flag: " + false == "flag: False"

# ── Array of one element ──
single = {42}
assert single{0} == 42
total = 0
loop x in single
    total += x
end
assert total == 42

# ── Nested array via intermediate (no chained access) ──
a = {{{42}}}
a0 = a{0}
a00 = a0{0}
assert a00{0} == 42

# ── Deeply nested if (pre-declare) ──
deep_val = "not reached"
if true
    if true
        if true
            if true
                if true
                    deep_val = "reached"
                end
            end
        end
    end
end
assert deep_val == "reached"

# ── Loop with 0 iterations ──
ran = false
loop i in 5..5
    ran = true
end
assert !ran

# ── Loop with step larger than range ──
count = 0
loop i in 0..5 by 10
    count += 1
end
assert count == 1

# ── Break on first iteration ──
first_only = {}
loop i in 0..100
    first_only << i
    break
end
assert first_only == {0}

# ── Next on every iteration ──
ran3 = false
loop i in 0..5
    next
    ran3 = true
end
assert !ran3

# ── Class with same-named property as function param ──
class Holder [val]
    fun set [val]
        this :: val = val
    end
    fun get []
        return this :: val
    end
end
h = new Holder [10]
assert h :: get [] == 10
h :: set [99]
assert h :: get [] == 99

# ── Function call as argument to another function ──
fun outer_fn [x]
    return x + 1
end
fun inner_fn [x]
    return x * 2
end
assert outer_fn [inner_fn [5]] == 11
assert inner_fn [outer_fn [5]] == 12

# ── Chained property access and method calls ──
class Wrapper [inner]
    fun get []
        return this :: inner
    end
end
class ValueBox [n]
    fun doubled []
        return n * 2
    end
end
v = new ValueBox [21]
w = new Wrapper [v]
inner_val = w :: get []
assert inner_val :: doubled [] == 42

# ── Reassigning to different types ──
x = 42
assert x == 42
x = "hello"
assert x == "hello"
x = true
assert x == true
x = null
assert x == null
x = {1, 2, 3}
assert x == {1, 2, 3}

# ── Multiple assignments ──
a = 1
b = 2
c = a + b
assert c == 3

# ── Fibonacci stress test ──
fun fib [n]
    if n < 2
        return n
    end
    return fib [n - 1] + fib [n - 2]
end
assert fib [15] == 610

# ── Exception in deeply nested call ──
fun level3 []
    raise "deep error"
end
fun level2 []
    return level3 []
end
fun level1 []
    return level2 []
end

caught = false
caught_val = null
begin
    level1 []
rescue e
    caught = true
    caught_val = e
end
assert caught
assert caught_val == "deep error"

# ── Array modification during iteration via index ──
arr = {1, 2, 3, 4, 5}
loop i in 0..5
    if arr{i} % 2 == 0
        arr{i} = 0
    end
end
assert arr == {1, 0, 3, 0, 5}

# ── Class instance stored in array, modified, re-read ──
class Mutable [val]
end
arr = {}
arr << new Mutable [1]
arr << new Mutable [2]
arr << new Mutable [3]
arr{1} :: val = 99
assert arr{0} :: val == 1
assert arr{1} :: val == 99
assert arr{2} :: val == 3

# ── Interpolation with nested expressions ──
arr = {10, 20, 30}
idx = 1
msg = "arr[{idx}] = {arr{idx}}"
assert msg == "arr[1] = 20"

# ── Ensure runs with no exception and no rescue ──
ensure_flag = false
begin
    x = 1
ensure
    ensure_flag = true
end
assert ensure_flag

# ── Comment handling ──
# This is a comment
x = 42
assert x == 42
# another comment
assert true
