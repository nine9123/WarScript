# ──────────────────────────────────────────────────────
# test_loops.ws
# Covers: while loop, for loop, for with step, iterable
#         loop, break, next, nested loops, loop + array
# ──────────────────────────────────────────────────────

# ── While loop basic ──
i = 0
sum = 0
loop i < 5
    sum = sum + i
    i = i + 1
end
assert sum == 10

# ── While loop doesn't execute if false ──
ran = false
loop false
    ran = true
end
assert !ran

# ── While loop single iteration ──
count = 0
condition = true
loop condition
    count = count + 1
    condition = false
end
assert count == 1

# ── For loop basic ──
total = 0
loop i in 0..5
    total += i
end
assert total == 10

# ── For loop zero iterations ──
total = 0
loop i in 5..5
    total += 1
end
assert total == 0

# ── For loop single iteration ──
total = 0
loop i in 0..1
    total += 1
end
assert total == 1

# ── For loop with step ──
collected = {}
loop i in 0..10 by 2
    collected << i
end
assert collected == {0, 2, 4, 6, 8}

# ── For loop with step of 3 ──
collected2 = {}
loop i in 0..15 by 3
    collected2 << i
end
assert collected2 == {0, 3, 6, 9, 12}

# ── For loop with step of 1 (explicit) ──
collected3 = {}
loop i in 0..3 by 1
    collected3 << i
end
assert collected3 == {0, 1, 2}

# ── For loop with variable bounds ──
lo = 2
hi = 7
result = {}
loop i in lo..hi
    result << i
end
assert result == {2, 3, 4, 5, 6}

# ── For loop with expression bounds ──
base_val = 5
result2 = {}
loop i in 0..base_val * 2
    result2 << i
end
assert result2 == {0, 1, 2, 3, 4, 5, 6, 7, 8, 9}

# ── Iterable loop over array ──
arr = {10, 20, 30, 40}
total = 0
loop item in arr
    total += item
end
assert total == 100

# ── Iterable loop collects items ──
source = {"a", "b", "c"}
upper_arr = {}
loop s in source
    upper_arr << s + "!"
end
assert upper_arr == {"a!", "b!", "c!"}

# ── Iterable loop over empty array ──
empty = {}
ran_empty = false
loop x in empty
    ran_empty = true
end
assert !ran_empty

# ── Break statement ──
result = {}
loop i in 0..10
    if i == 5
        break
    end
    result << i
end
assert result == {0, 1, 2, 3, 4}

# ── Break in while loop ──
j = 0
loop true
    if j >= 3
        break
    end
    j += 1
end
assert j == 3

# ── Break only exits innermost loop ──
outer_runs = 0
loop i in 0..3
    outer_runs += 1
    loop j in 0..10
        if j == 2
            break
        end
    end
end
assert outer_runs == 3

# ── Next statement (skip iteration) ──
result = {}
loop i in 0..8
    if i % 2 == 0
        next
    end
    result << i
end
assert result == {1, 3, 5, 7}

# ── Next skips to next iteration, not break ──
result2 = {}
loop i in 0..5
    if i == 2
        next
    end
    result2 << i
end
assert result2 == {0, 1, 3, 4}

# ── Nested for loops ──
pairs = {}
loop i in 0..3
    loop j in 0..3
        if i != j
            pairs << i * 10 + j
        end
    end
end
assert pairs == {1, 2, 10, 12, 20, 21}

# ── Loop building 2D pattern ──
grid = {}
loop row in 0..3
    row_arr = {}
    loop col in 0..3
        row_arr << row * 3 + col
    end
    grid << row_arr
end
assert grid{0} == {0, 1, 2}
assert grid{1} == {3, 4, 5}
assert grid{2} == {6, 7, 8}

# ── Loop with function call ──
fun square [n]
    return n * n
end
squares = {}
loop i in 0..6
    squares << square [i]
end
assert squares == {0, 1, 4, 9, 16, 25}

# ── Loop modifying array in place ──
arr = {1, 2, 3, 4, 5}
loop i in 0..5
    arr{i} = arr{i} * 10
end
assert arr == {10, 20, 30, 40, 50}

# ── While loop counting down ──
n = 10
steps = 0
loop n > 0
    n -= 3
    steps += 1
end
assert steps == 4

# ── Nested break + next ──
result = {}
loop i in 0..5
    if i == 1
        next
    end
    if i == 4
        break
    end
    result << i
end
assert result == {0, 2, 3}

# ── Loop with multiple conditions ──
result = {}
loop i in 0..20 by 1
    if i % 3 == 0 and i % 5 == 0
        result << "fizzbuzz"
    elif i % 3 == 0
        result << "fizz"
    elif i % 5 == 0
        result << "buzz"
    else
        result << i
    end
end
assert result{0} == "fizzbuzz"
assert result{3} == "fizz"
assert result{5} == "buzz"
assert result{1} == 1
assert result{15} == "fizzbuzz"
