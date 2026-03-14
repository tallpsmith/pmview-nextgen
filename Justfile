set dotenv-load
# do clean & generate
doit: clean generate

# Generate a scene in an external Godot test project
generate:
	dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- --pmproxy http://$PMPROXY_HOST:44322 --install-addon -o $GODOT_PROJECT

# Remove any of the Add-on or generated host scenes
clean:
	rm -rf $GODOT_PROJECT/addons/pmview*
	rm $GODOT_PROJECT/host*.tscn
