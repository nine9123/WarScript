# Compound operations: +=, -=, *=, /= in every context,
# including class properties, array elements, inside
# loops, functions, and combined with other operations

# ── 1. All four compound ops ──
x = 100
x += 50
assert x == 150
x -= 30
assert x == 120
x *= 2
assert x == 240
x /= 8
assert x == 30

# ── 2. Compound on array elements ──
arr = {10, 20, 30, 40, 50}
arr{0} += 5
arr{1} -= 5
arr{2} *= 2
arr{3} /= 4
assert arr == {15, 15, 60, 10, 50}

# ── 3. Compound on class properties ──
class Player [hp, mp, gold]
end

p = new Player [100, 50, 0]
p :: hp -= 25
p :: mp -= 10
p :: gold += 100
assert p :: hp == 75
assert p :: mp == 40
assert p :: gold == 100

p :: hp *= 2
assert p :: hp == 150
p :: gold /= 4
assert p :: gold == 25

# ── 4. Compound in for loop ──
sum = 0
product = 1
loop i in 1..6
    sum += i
    product *= i
end
assert sum == 15
assert product == 120

# ── 5. Compound in while loop ──
x = 1000
count = 0
loop x > 1
    x /= 2
    count += 1
end
assert count == 10

# ── 6. String compound concatenation ──
msg = "Hello"
msg += " "
msg += "World"
msg += "!"
assert msg == "Hello World!"

# ── 7. Compound in function ──
fun running_total [arr, n]
    total = 0
    result = {}
    loop i in 0..n
        total += arr{i}
        result << total
    end
    return result
end

assert running_total [{1, 2, 3, 4, 5}, 5] == {1, 3, 6, 10, 15}

# ── 8. Compound with expressions on RHS ──
x = 10
x += 3 * 2
assert x == 16
x -= 1 + 1
assert x == 14
x *= 1 + 1
assert x == 28
x /= 2 + 5
assert x == 4

# ── 9. Compound on multiple class instances ──
units = {}
loop i in 0..3
    units << new Player [100, 50, 0]
end
units{0} :: hp -= 50
units{1} :: hp -= 25
units{2} :: gold += 500
assert units{0} :: hp == 50
assert units{1} :: hp == 75
assert units{2} :: gold == 500
assert units{0} :: gold == 0

# ── 10. Countdown with compound ──
values = {}
n = 50
loop n > 0
    values << n
    n -= 15
end
assert values == {50, 35, 20, 5}

# ── 11. Alternating compound ops ──
x = 1
loop i in 0..4
    x += i
    x *= 2
end
# i=0: x=1+0=1, x=1*2=2
# i=1: x=2+1=3, x=3*2=6
# i=2: x=6+2=8, x=8*2=16
# i=3: x=16+3=19, x=19*2=38
assert x == 38
