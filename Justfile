set dotenv-load

# Generate a scene in an external Godot test project
generate:
	dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- --pmproxy http://$PMPROXY_HOST:44322 --install-addon -o $GODOT_PROJECT

clean:
	rm -rf $GODOT_PROJECT/addons/pmview*
	rm $GODOT_PROJECT/host*.tscn
