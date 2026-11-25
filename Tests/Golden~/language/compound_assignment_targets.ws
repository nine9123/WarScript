# T3.13, PROP-32: every compound assignment target without a side effect in
# its twice-evaluated parts compiles and behaves as before; the right-hand
# side runs once.
calls = 0
fun g()
    global calls
    calls += 1
    return 1
end

class P(hp)
    items = [[1, 2], [3, 4]]
    counts = [1, 2]
    fun hurt(k)
        @hp -= 1
        this.counts[k] *= 2
        return @hp
    end
end

x = 1
x += 1
print x
a = [1, 2]
i = 1
a[i] += 1
print a
grid = [[1, 2], [3, 4]]
grid[1][0] += 10
print grid
p = new P(10)
p.hp -= 5
print p.hp
p.items[0][1] += 1
print p.items
print p.hurt(1)
print p.counts
a[0] += g()
print a
print calls
print 1 + (a[0] += 1)
print a
row = [5]
row[0] += 1
print row
b = [2]
b[0] -= 1
b[0] *= 8
b[0] /= 2
print b
# expect: 2
# expect: [1, 3]
# expect: [[1, 2], [13, 4]]
# expect: 5
# expect: [[1, 3], [3, 4]]
# expect: 4
# expect: [1, 4]
# expect: [2, 3]
# expect: 1
# expect: 4
# expect: [3, 3]
# expect: [6]
# expect: [4]
