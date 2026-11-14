# Loop patterns: complex iteration, nested break/next,
# while-loop state machines, loop with exception handling,
# array building patterns, countdown, search patterns

# ── 1. Nested loop with outer break via flag ──
found_pair = false
found_i = -1
found_j = -1
loop i in 0..5
    if found_pair
        break
    end
    loop j in i + 1..5
        if i + j == 5
            found_pair = true
            found_i = i
            found_j = j
            break
        end
    end
end
assert found_pair
assert found_i == 1
assert found_j == 4

# ── 2. Generate multiplication table ──
table = {}
loop i in 1..4
    row = {}
    loop j in 1..4
        row << i * j
    end
    table << row
end
r0 = table{0}
r1 = table{1}
r2 = table{2}
assert r0 == {1, 2, 3}
assert r1 == {2, 4, 6}
assert r2 == {3, 6, 9}

# ── 3. Collect first N primes via trial division ──
fun is_prime_check [n]
    if n < 2
        return false
    end
    d = 2
    loop d * d <= n
        if n % d == 0
            return false
        end
        d += 1
    end
    return true
end

primes = {}
candidate = 2
loop candidate < 30
    if is_prime_check [candidate]
        primes << candidate
    end
    candidate += 1
end
assert primes == {2, 3, 5, 7, 11, 13, 17, 19, 23, 29}

# ── 4. Countdown with accumulation ──
path = {}
n = 10
loop n > 0
    path << n
    n -= 3
end
assert path == {10, 7, 4, 1}

# ── 5. Loop with exception per iteration ──
results = {}
loop i in 0..5
    ok = true
    begin
        if i == 2 or i == 4
            raise "skip"
        end
        results << "ok:" + i
    rescue e
        results << "err:" + i
        ok = false
    end
end
assert results == {"ok:0", "ok:1", "err:2", "ok:3", "err:4"}

# ── 6. Nested for loops with step ──
result = {}
loop i in 0..10 by 3
    loop j in 0..6 by 2
        result << i * 10 + j
    end
end
assert result == {0, 2, 4, 30, 32, 34, 60, 62, 64, 90, 92, 94}

# ── 7. While loop simulating do-while ──
x = 0
loop true
    x += 1
    if x >= 5
        break
    end
end
assert x == 5

# ── 8. Loop building string pattern ──
pattern = ""
loop i in 0..5
    loop j in 0..i + 1
        pattern += "*"
    end
    pattern += "|"
end
assert pattern == "*|**|***|****|*****|"

# ── 9. Iterable loop over array of arrays ──
matrix = {{1, 2}, {3, 4}, {5, 6}}
flat = {}
loop row in matrix
    loop val in row
        flat << val
    end
end
assert flat == {1, 2, 3, 4, 5, 6}

# ── 10. Complex loop: group consecutive equal elements ──
fun group_consecutive [arr, n]
    groups = {}
    if n == 0
        return groups
    end
    current_group = {arr{0}}
    loop i in 1..n
        prev = arr{i - 1}
        if arr{i} == prev
            current_group << arr{i}
        else
            groups << current_group
            current_group = {arr{i}}
        end
    end
    groups << current_group
    return groups
end

g = group_consecutive [{1, 1, 2, 2, 2, 3, 1, 1}, 8]
assert g{0} == {1, 1}
assert g{1} == {2, 2, 2}
assert g{2} == {3}
assert g{3} == {1, 1}

g2 = group_consecutive [{5}, 1]
assert g2{0} == {5}

g3 = group_consecutive [{"a", "a", "b"}, 3]
assert g3{0} == {"a", "a"}
assert g3{1} == {"b"}
