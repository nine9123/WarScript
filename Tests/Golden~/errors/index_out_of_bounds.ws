# T2.5/T3.6: an index past the end raises IndexError (DEC-10, plan 5.5).
arr = [1, 2, 3]
x = arr[3]   # expect-error: IndexError: Index 3 is out of bounds (length 3)
