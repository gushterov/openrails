These gzip-compressed replay files were generated with .NET 6 and the fork's
pre-migration assemblies (95b41d590 command schema), using BinaryFormatter.
They are fixed compatibility fixtures; the .NET 10 tests never invoke that serializer.

- `net6-all-commands`: all 155 concrete simulation/viewer command types, ordered by
  full type name. Every instance field, including inherited/private fields, is set
  to 7 for numeric types, true for bool, "legacy-test" for string, or the first enum
  value. Nullable fields contain a value. Constructors and Redo are never called.
- `net6-brake-flood`: a List<ICommand> with capacity 32768 containing 10000 identical
  TrainBrakeCommands at time 100 (ToState=false, Target=0), followed by one at time
  200 (ToState=true, Target=0.75). Verifies legacy log compaction.
- `net6-empty`: an empty List<ICommand>.
