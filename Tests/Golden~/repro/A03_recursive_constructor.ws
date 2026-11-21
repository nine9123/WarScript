# isolate
# budget: 100000
# Plan A.3 (T1.1): v1 aborted the process even with the budget set; since
# T1.1 constructors are frames on the current VM and hit FramesMax.
# Still isolated, so that a regression cannot take the test host down.
class Node [v]
    child = new Node [v + 1]
end
begin
    n = new Node [0]
rescue e
    print "rescued " + e      # expect: rescued Stack overflow
end
