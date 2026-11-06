# ──────────────────────────────────────────────────────
# test_functions.ws
# Covers: definition, arguments, return values, recursion,
#         no-arg functions, multiple returns, nested calls,
#         functions as values, closures over globals
# ──────────────────────────────────────────────────────

# ── Basic function definition and call ──
fun add [a, b]
    return a + b
end
assert add [3, 4] == 7

# ── No-argument function ──
fun get_pi
    return 3.14159
end
assert get_pi [] == 3.14159

# ── Function with no explicit return ──
fun do_nothing
end
# should not crash, returns null

# ── Function returning different types ──
fun identity [x]
    return x
end
assert identity [42] == 42
assert identity ["hello"] == "hello"
assert identity [true] == true
assert identity [null] == null

# ── Multiple parameters ──
fun sum3 [a, b, c]
    return a + b + c
end
assert sum3 [1, 2, 3] == 6

fun sum5 [a, b, c, d, e]
    return a + b + c + d + e
end
assert sum5 [10, 20, 30, 40, 50] == 150

# ── Early return ──
fun abs_val [n]
    if n < 0
        return -n
    end
    return n
end
assert abs_val [5] == 5
assert abs_val [-5] == 5
assert abs_val [0] == 0

# ── Multiple return paths ──
fun classify [n]
    if n > 0
        return "positive"
    elif n < 0
        return "negative"
    else
        return "zero"
    end
end
assert classify [5] == "positive"
assert classify [-3] == "negative"
assert classify [0] == "zero"

# ── Recursion: factorial ──
fun factorial [n]
    if n <= 1
        return 1
    end
    return n * factorial [n - 1]
end
assert factorial [0] == 1
assert factorial [1] == 1
assert factorial [5] == 120
assert factorial [10] == 3628800

# ── Recursion: fibonacci ──
fun fib [n]
    if n < 2
        return n
    end
    return fib [n - 1] + fib [n - 2]
end
assert fib [0] == 0
assert fib [1] == 1
assert fib [2] == 1
assert fib [5] == 5
assert fib [10] == 55

# ── Mutual recursion ──
fun is_even_rec [n]
    if n == 0
        return true
    end
    return is_odd_rec [n - 1]
end

fun is_odd_rec [n]
    if n == 0
        return false
    end
    return is_even_rec [n - 1]
end

assert is_even_rec [4]
assert !is_even_rec [3]
assert is_odd_rec [5]
assert !is_odd_rec [6]

# ── Function calling other functions ──
fun double [n]
    return n * 2
end

fun quadruple [n]
    return double [double [n]]
end
assert quadruple [3] == 12
assert quadruple [0] == 0

# ── Functions with array parameters ──
fun array_sum [arr, len]
    total = 0
    loop i in 0..len
        total += arr{i}
    end
    return total
end
assert array_sum [{1, 2, 3, 4, 5}, 5] == 15

# ── Functions that modify arrays ──
fun append_doubled [arr, val]
    arr << val * 2
end
my_arr = {1, 2}
append_doubled [my_arr, 5]
assert my_arr == {1, 2, 10}

# ── Function returning array ──
fun make_range [n]
    result = {}
    loop i in 0..n
        result << i
    end
    return result
end
assert make_range [5] == {0, 1, 2, 3, 4}

# ── Functions with loops and conditions ──
fun find_first [arr, target, len]
    loop i in 0..len
        if arr{i} == target
            return i
        end
    end
    return -1
end
assert find_first [{10, 20, 30, 40}, 30, 4] == 2
assert find_first [{10, 20, 30, 40}, 50, 4] == -1

# ── Function as expression argument ──
assert add [factorial [3], factorial [4]] == 30

# ── Nested function calls in expressions ──
assert add [1, add [2, add [3, 4]]] == 10

# ── Function modifying global state ──
global_counter = 0
fun bump []
    global_counter += 1
end
bump []
bump []
bump []
assert global_counter == 3

# ── Function with boolean parameters ──
fun check [flag, val]
    if flag
        return val
    end
    return 0
end
assert check [true, 42] == 42
assert check [false, 42] == 0

# ── Recursive data processing ──
fun power [base, exp]
    if exp == 0
        return 1
    end
    return base * power [base, exp - 1]
end
assert power [2, 0] == 1
assert power [2, 1] == 2
assert power [2, 10] == 1024
assert power [3, 3] == 27
