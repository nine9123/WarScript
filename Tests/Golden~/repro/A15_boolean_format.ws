# known-failing: SEM-9
# Plan A.15 (DEC-11). v1 prints True / f: False.
print true            # expect: true
print "f: " + false   # expect: f: false
