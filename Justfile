set dotenv-load
# do clean & generate
doit: clean generate

# Scaffold a new Godot project from scratch
init:
	@dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- init $GODOT_PROJECT

# Generate a host-view scene into an existing Godot project
generate:
	@dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- --pmproxy http://$PMPROXY_HOST:$PMPROXY_PORT -o $GODOT_PROJECT/scenes/host_view.tscn

# Scaffold + generate in one shot
init-generate:
	@dotnet run --project src/pmview-host-projector/src/PmviewHostProjector -- --init --pmproxy http://$PMPROXY_HOST:$PMPROXY_PORT -o $GODOT_PROJECT/scenes/host_view.tscn

# Remove any of the Add-on or generated host scenes
clean:
	@echo "Removing addon"
	@rm -rf $GODOT_PROJECT/addons/pmview*
	@echo "Removing scenes"
	@rm -f $GODOT_PROJECT/scenes/host*.tscn
	@rm -f $GODOT_PROJECT/host*.tscn
