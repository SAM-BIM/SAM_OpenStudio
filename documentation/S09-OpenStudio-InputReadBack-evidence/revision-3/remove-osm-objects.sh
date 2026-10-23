#!/usr/bin/env bash
# SPDX-License-Identifier: LGPL-3.0-or-later
# Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors
# Usage: remove-osm-objects.sh <run dir> <OS type>. Removes every object of that type from model.osm (line-based, the
# object runs from its "<type>," line through the line whose data part ends with ';'), then rewrites export.json OsmSha256.
set -e
dir=$1; type=$2
awk -v t="$type," '
  BEGIN{skip=0}
  { line=$0; sub(/\r$/,"",line); data=line; i=index(data,"!"); if(i>0) data=substr(data,1,i-1)
    if(!skip && line ~ "^[ \t]*" && line==t){skip=1; n++; next}
    if(skip){ if(data ~ /;[ \t]*$/){skip=0; drop=1}; next }
    if(drop && line==""){drop=0; next} drop=0; print }
  END{ if(n==0) exit 3; print "removed " n " " t > "/dev/stderr" }' "$dir/model.osm" > "$dir/model.osm.new"
mv "$dir/model.osm.new" "$dir/model.osm"
h=$(sha256sum "$dir/model.osm" | cut -d' ' -f1)
sed -i -E "s/(\"OsmSha256\": \")[0-9a-f]+/\1$h/; s/(\"Mutation\": )null/\1\"reviewer-style: every $type removed from model.osm\"/" "$dir/export.json"
grep -E '"(OsmSha256|Mutation)"' "$dir/export.json"
