# T3.8 (PROP-29): a class's constructor parameters too.
class Point(x, y, x)   # expect-compile-error: Parameter 'x' is declared twice
end
print (new Point(1, 2, 3)).y
