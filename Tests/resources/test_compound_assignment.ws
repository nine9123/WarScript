# ──────────────────────────────────────────────────────
# test_compound_assignment.ws
# Covers: +=, -=, *=, /= on variables, arrays,
#         class properties, in loops, chained
# ──────────────────────────────────────────────────────

# ── Basic += ──
x = 10
x += 5
assert x == 15

# ── Basic -= ──
x = 10
x -= 3
assert x == 7

# ── Basic *= ──
x = 10
x *= 4
assert x == 40

# ── Basic /= ──
x = 10
x /= 4
assert x == 2.5

# ── Chained compound assignments ──
x = 100
x += 10
x -= 5
x *= 2
x /= 5
assert x == 42

# ── Compound with expressions ──
x = 10
x += 2 * 3
assert x == 16

x = 100
x -= 5 + 5
assert x == 90

x = 3
x *= 2 + 1
assert x == 9

# ── Compound in for loop ──
sum = 0
loop i in 0..10
    sum += i
end
assert sum == 45

# ── Compound in while loop ──
count = 100
loop count > 0
    count -= 10
end
assert count == 0

# ── Compound *= in loop (factorial) ──
result = 1
loop i in 1..6
    result *= i
end
assert result == 120

# ── += with strings ──
msg = "hello"
msg += " world"
assert msg == "hello world"

msg += "!"
assert msg == "hello world!"

# ── Compound on array elements ──
arr = {10, 20, 30}
arr{0} += 5
assert arr{0} == 15

arr{1} -= 10
assert arr{1} == 10

arr{2} *= 2
assert arr{2} == 60

# ── Compound on class properties ──
class Entity [hp, attack]
end

e = new Entity [100, 10]
e :: hp -= 25
assert e :: hp == 75

e :: attack += 5
assert e :: attack == 15

e :: hp -= e :: attack
assert e :: hp == 60

# ── Compound inside functions ──
fun accumulate [n]
    total = 0
    loop i in 0..n
        total += i
    end
    return total
end
assert accumulate [10] == 45
assert accumulate [5] == 10
assert accumulate [1] == 0

# ── Multiple compound ops on same variable ──
v = 1
v += 1
v *= 10
v -= 5
v /= 3
assert v == 5

# ── Compound assignment preserves type for decimals ──
d = 1.0
d += 0.5
assert d == 1.5
d *= 2
assert d == 3
d /= 4
assert d == 0.75
