# known-failing: SEM-5
# Plan A.17 (DEC-4). v1 prints null.
if true
    made = 1
end
print made    # expect: 1
