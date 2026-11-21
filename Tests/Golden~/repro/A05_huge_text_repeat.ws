# Plan A.5 (T1.4): v1 let an ArgumentOutOfRangeException escape; v2 raises
# LimitExceeded "Text too long", which the script rescues.
begin
    x = "ab" * 2000000000
rescue e
    print "rescued {e}"   # expect: rescued Text too long
end
