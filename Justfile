# Generate a scene in an external Godot test project
generate:
	dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- --pmproxy http://localhost:44322 --install-addon -o ../pmview-godot-test

