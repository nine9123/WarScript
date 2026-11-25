# T3.12: calls and parameter lists use (), arrays and indexes [], members '.'.
# A '(' right after a name or 'fun' is a call or parameter list; any other '('
# groups. A '[' after an operand indexes it, so indexes chain.
fun add(a, b = 10)
    return a + b
end
print add(1, 2)
print add (1)
print (1 + 2) * 3

grid = [[1, 2], [3, 4]]
print grid[1][0]
grid[0][1] = 9
print grid

fun rows()
    return grid
end
print rows()[1][1]
print [5, 6, 7][2]
print (grid[1])[0]
print "abc"[1]
print "x{grid[0][1]}y"[1]
print -grid[1][1] + 1

class Box(items)
    fun first()
        return @items[0]
    end
end
b = new Box([[1, 2], [3]])
print b.items[0][1]
b.items[0][1] = 8
print b.first()[1]
boxes = [b]
print boxes[0].first()

# expect: 3
# expect: 11
# expect: 9
# expect: 3
# expect: [[1, 9], [3, 4]]
# expect: 4
# expect: 7
# expect: 3
# expect: b
# expect: 9
# expect: -3
# expect: 2
# expect: 8
# expect: [1, 8]
