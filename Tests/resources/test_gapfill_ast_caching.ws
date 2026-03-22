# ──────────────────────────────────────────────────────
# test_gapfill_ast_caching.ws
# Tests behaviors that could break if AST is cached and
# reused: variable state, function redefinition, class
# instances across executions.
# ──────────────────────────────────────────────────────

# ── Variables should initialize fresh each run ──
assert undefined_var == null
x = 10
assert x == 10

# ── Functions defined in the script should work ──
fun double[n]
    return n * 2
end
assert double[5] == 10
assert double[0] == 0
assert double[-3] == -6

# ── Classes should be instantiable ──
class Pos[x, y]
end
p = new Pos[1, 2]
assert p :: x == 1
assert p :: y == 2

# ── Loops should run correctly ──
total = 0
loop i in 0..100
    total += i
end
assert total == 4950

# ── Nested function calls ──
fun fib[n]
    if n < 2
        return n
    end
    return fib[n - 1] + fib[n - 2]
end
assert fib[10] == 55

# ── Exception handling ──
caught = false
begin
    raise "test error"
rescue e
    caught = true
    assert e == "test error"
end
assert caught == true

# ── String interpolation ──
name = "World"
assert "Hello, {name}!" == "Hello, World!"

# ── Array operations ──
arr = {}
loop i in 0..5
    arr << i * i
end
assert arr{0} == 0
assert arr{1} == 1
assert arr{2} == 4
assert arr{3} == 9
assert arr{4} == 16
