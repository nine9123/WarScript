# known-failing: CRASH-3
# Plan A.5 (T1.4): v1 lets an ArgumentOutOfRangeException escape; v2 raises
# LimitExceeded "Text too long", which the script rescues.
begin
    x = "ab" * 2000000000
rescue e
    print "rescued"   # expect: rescued
end
