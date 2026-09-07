using var command = new SnapshotCommand();

_ = command.ExecuteScalar();

Probe.Emit(command);

return 0;
