# ── 1. Basic default value ──
fun greet [name, greeting = "Hello"]
    return greeting + ", " + name + "!"
end

assert greet["World"] == "Hello, World!"
assert greet["World", "Hi"] == "Hi, World!"

# ── 2. Multiple defaults ──
fun make_point [x, y = 0, z = 0]
    return x + y + z
end

assert make_point[10] == 10
assert make_point[10, 20] == 30
assert make_point[10, 20, 30] == 60

# ── 3. Default with expression ──
fun add [a, b = 1 + 1]
    return a + b
end

assert add[5] == 7
assert add[5, 10] == 15

# ── 4. All params have defaults ──
fun optional [a = 1, b = 2, c = 3]
    return a + b + c
end

assert optional[] == 6
assert optional[10] == 15
assert optional[10, 20] == 33
assert optional[10, 20, 30] == 60

# ── 5. Default with string ──
fun tag [value, prefix = "item_"]
    return prefix + value
end

assert tag["sword"] == "item_sword"
assert tag["sword", "weapon_"] == "weapon_sword"

# ── 6. Default value is null-triggered (explicit null gets default) ──
fun safe [x, fallback = 42]
    return fallback
end

assert safe["anything"] == 42
assert safe["anything", 99] == 99

# ── 7. Method on a class with defaults ──
class Counter [n]
    fun add [amount = 1]
        n = n + amount
    end
    fun get []
        return this :: n
    end
end

c = new Counter [0]
c :: add []
assert c :: get [] == 1
c :: add [5]
assert c :: get [] == 6

# ── 8. Default with boolean ──
fun maybe [value, flag = true]
    if flag
        return value
    end
    return null
end

assert maybe[42] == 42
assert maybe[42, false] == null

# ── 9. Recursive function with default ──
fun countdown [n, acc = 0]
    if n == 0
        return acc
    end
    return countdown [n - 1, acc + n]
end

assert countdown[5] == 15
assert countdown[5, 100] == 115

# ── 10. Default interacts correctly with named args ──
fun rect_area [width, height = 10]
    return width * height
end

assert rect_area[5] == 50
assert rect_area[width: 5] == 50
assert rect_area[width: 5, height: 3] == 15

print "all default parameter tests passed"
