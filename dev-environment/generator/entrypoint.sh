#!/usr/bin/env bash
set -euo pipefail

for profile in /profiles/*.yml /profiles/*.yaml; do
    [[ -f "${profile}" ]] || continue
    stem=$(basename "${profile}")
    stem="${stem%.yml}"
    stem="${stem%.yaml}"

    # Fleet profiles use the `fleet` subcommand and produce a directory of archives
    if [[ "${stem}" == fleet-* ]]; then
        echo "INFO: generating fleet archives for ${profile} → /archives/${stem}/"
        if ! pmlogsynth fleet --seed 42 -o "/archives/${stem}" "${profile}"; then
            echo "ERROR: pmlogsynth fleet failed for ${profile}"
            exit 1
        fi
        echo "INFO: fleet ${stem} complete"
    else
        mkdir -p "/archives/${stem}"
        echo "INFO: generating archive for ${profile} → /archives/${stem}/${stem}"
        if ! pmlogsynth -o "/archives/${stem}/${stem}" "${profile}"; then
            echo "ERROR: pmlogsynth failed for ${profile}"
            exit 1
        fi
        echo "INFO: archive ${stem} complete"
    fi
done

echo "INFO: all profiles generated successfully"
