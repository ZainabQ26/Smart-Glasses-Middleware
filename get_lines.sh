#!/bin/bash
find src/SmartGlassesMiddleware -name "*.cs" | grep -v "Migrations" | grep -v "obj/" | xargs wc -l | sort -n
