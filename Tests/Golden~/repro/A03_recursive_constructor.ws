# isolate
# known-failing: CRASH-2
# budget: 100000
# Plan A.3 (T1.1): v1 aborts the process even with the budget set.
class Node [v]
    child = new Node [v + 1]
end
begin
    n = new Node [0]
rescue e
    print "rescued " + e      # expect: rescued Stack overflow
end
