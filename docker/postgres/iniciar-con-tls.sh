#!/bin/sh
# PostgreSQL con TLS para la instalación con Docker Compose: la app se conecta con "SSL Mode=Require".
# La primera vez genera un certificado propio de esta instalación (no viene en la imagen ni en el repositorio)
# y lo guarda en un volumen; después lo reutiliza.
set -eu
DIR=/var/lib/postgresql/certs
if [ ! -s "$DIR/server.key" ]; then
  mkdir -p "$DIR"
  openssl req -new -x509 -days 3650 -nodes -subj "/CN=db" -keyout "$DIR/server.key" -out "$DIR/server.crt" >/dev/null 2>&1
  chown -R postgres:postgres "$DIR"
  chmod 600 "$DIR/server.key"
fi
exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file="$DIR/server.crt" -c ssl_key_file="$DIR/server.key"
