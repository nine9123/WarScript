# known-failing: CRASH-7
# Plan A.24 (found by the fuzzer, T0.7): an error raised inside 'ensure'
# while another error is propagating leaves v1's exception state raised with
# no exception; the rescue does not run and Run() throws a
# NullReferenceException to the host. The error raised in 'ensure' replaces
# the original one, as it does in v1 when nothing rescues it.
begin
    begin
        raise "first"
    ensure
        raise "second"
    end
rescue e
    print "rescued " + e   # expect: rescued second
end
