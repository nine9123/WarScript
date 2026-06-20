# --- multiline function calls ---

fun add[a, b]
  return a + b
end

fun sum3[a, b, c]
  return a + b + c
end

fun sum5[a, b, c, d, e]
  return a + b + c + d + e
end

fun greet[name, greeting]
  return greeting + " " + name
end

# Basic single-line still works
result1 = add[10, 20]
print result1

# Two args across two lines
result2 = add[
  10,
  20
]
print result2

# Three args across three lines
result3 = sum3[
  1,
  2,
  3
]
print result3

# Five args across five lines (the original motivation)
result4 = sum5[
  1,
  2,
  3,
  4,
  5
]
print result4

# Trailing comma — should be accepted
result5 = sum3[
  10,
  20,
  30,
]
print result5

# Expressions as multiline args
x = 5
y = 3
result6 = sum3[
  x * 2,
  y + 1,
  10
]
print result6

# Nested calls as args
result7 = add[
  add[1, 2],
  add[3, 4]
]
print result7

# String args across lines
msg = greet[
  "World",
  "Hello"
]
print msg
