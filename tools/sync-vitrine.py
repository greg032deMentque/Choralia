"""
Resynchronise le depot vitrine public (Choralia) depuis le depot de travail prive
(ChoraleHelper).

Pourquoi un script plutot qu'un copier-coller : la vitrine est PUBLIQUE. Une copie
brute du dossier y emporterait appsettings.Development.json, les logs applicatifs et
les dossiers bin/, qui contiennent des secrets. Ce script ne copie QUE l'ensemble
que git considere comme propre, puis il relit le resultat a la recherche de secrets
et refuse de rendre la main si un doute subsiste.

Usage :
    python tools/sync-vitrine.py

Le script NE COMMITE NI NE POUSSE RIEN : il prepare et verifie, tu relis et decides.
"""

import os
import re
import shutil
import subprocess
import sys

# Chemins DEDUITS, jamais codes en dur : `.claude/CLAUDE.md` interdit tout chemin absolu
# de poste dans un fichier versionne — le depot doit rester utilisable sur une autre
# machine. SOURCE est la racine du depot (ce script vit dans tools/). VITRINE se surcharge
# par la variable d'environnement CHORALIA_VITRINE, et vaut par defaut le dossier
# « Choralia » voisin de la racine.
SOURCE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VITRINE = os.environ.get(
    "CHORALIA_VITRINE",
    os.path.join(os.path.dirname(SOURCE), "Choralia"),
)

# Fichiers de la vitrine a NE JAMAIS ecraser : ils lui sont propres.
# README.md cote vitrine est la version orientee lecture externe ; celui du depot de
# travail est operationnel et nomme des ressources d'infrastructure.
PRESERVER = {"README.md"}

# Motifs cherches dans le resultat de la copie. Aucune valeur n'est jamais affichee :
# seuls le type, le fichier et la ligne le sont.
MOTIFS = [
    ("jeton type JWT / base64 long", re.compile(r"eyJ[A-Za-z0-9_\-]{30,}")),
    ('"Password" renseigne', re.compile(r'"[Pp]assword"\s*:\s*"(?!<REPLACE)[^"]{4,}"')),
    ('"Secret" renseigne', re.compile(r'"Secret"\s*:\s*"(?!<REPLACE)[^"]{8,}"')),
    ('"IpSalt" renseigne', re.compile(r'"IpSalt"\s*:\s*"(?!<REPLACE)[^"]{4,}"')),
    ("cle de stockage apparente", re.compile(r"(AccountKey|SharedAccessSignature|AKIA)\s*[=:]")),
    ("chaine de connexion Azure SQL", re.compile(r"[A-Za-z0-9\-]+\.database\.windows\.net")),
]

EXTENSIONS_SCANNEES = {
    ".cs", ".json", ".ts", ".js", ".yml", ".yaml", ".md", ".config", ".xml",
    ".props", ".html", ".scss", ".txt", ".http", ".slnx", ".csproj",
}

# Noms qui ne doivent JAMAIS se retrouver dans la vitrine, quelle qu'en soit la raison.
INTERDITS = re.compile(
    r"(appsettings\.(Development|Staging|Production|Local)\.json"
    r"|\.pfx$|\.pem$|\.p12$|(^|/)\.env|\.publishsettings$"
    r"|(^|/)(bin|obj|node_modules|dist|\.pnpm-store|Logs|storage)/)",
    re.IGNORECASE,
)


def git(args, cwd):
    r = subprocess.run(
        ["git"] + args, cwd=cwd, capture_output=True, text=True,
        encoding="utf-8", errors="replace",
    )
    if r.returncode != 0:
        raise SystemExit(f"echec de `git {' '.join(args)}` :\n{r.stderr}")
    return r.stdout


def titre(texte):
    print()
    print(texte)
    print("-" * len(texte))


def main():
    for chemin, libelle in ((SOURCE, "source"), (VITRINE, "vitrine")):
        if not os.path.isdir(os.path.join(chemin, ".git")):
            raise SystemExit(f"{libelle} introuvable ou sans depot git : {chemin}")

    # --- 1. Etat de la source ------------------------------------------------
    titre("1. Etat du depot de travail")
    sale = git(["status", "--porcelain"], SOURCE).strip()
    branche = git(["rev-parse", "--abbrev-ref", "HEAD"], SOURCE).strip()
    print(f"   branche : {branche}")
    if sale:
        n = len(sale.splitlines())
        print(f"   ATTENTION : {n} fichier(s) non commite(s).")
        print("   La vitrine refletera l'arbre de TRAVAIL, pas le dernier commit.")
        if input("   Continuer malgre tout ? [o/N] ").strip().lower() not in ("o", "oui"):
            raise SystemExit("interrompu.")
    else:
        print("   arbre propre")

    fichiers = [f for f in git(["ls-files"], SOURCE).splitlines() if f.strip()]

    # Ceinture et bretelles : meme si git suit un fichier interdit par erreur
    # (c'est deja arrive avec les logs applicatifs), il ne passera pas ici.
    ecartes = [f for f in fichiers if INTERDITS.search(f)]
    fichiers = [f for f in fichiers if not INTERDITS.search(f)]

    # --- 2. Copie ------------------------------------------------------------
    titre("2. Copie")
    attendus = set()
    copies = 0
    for rel in fichiers:
        if rel in PRESERVER:
            continue
        attendus.add(rel)
        src = os.path.join(SOURCE, rel.replace("/", os.sep))
        dst = os.path.join(VITRINE, rel.replace("/", os.sep))
        if not os.path.isfile(src):
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
        copies += 1
    print(f"   {copies} fichier(s) copie(s)")
    if ecartes:
        print(f"   {len(ecartes)} fichier(s) ecarte(s) par la liste d'interdits :")
        for f in ecartes[:10]:
            print(f"      - {f}")

    # --- 3. Suppression des fichiers disparus de la source -------------------
    # Sans cette etape, un fichier supprime du projet resterait indefiniment dans
    # la vitrine : elle divergerait en silence.
    titre("3. Fichiers disparus de la source")
    suivis_vitrine = [f for f in git(["ls-files"], VITRINE).splitlines() if f.strip()]
    obsoletes = [f for f in suivis_vitrine if f not in attendus and f not in PRESERVER]
    for rel in obsoletes:
        chemin = os.path.join(VITRINE, rel.replace("/", os.sep))
        if os.path.isfile(chemin):
            os.remove(chemin)
    print(f"   {len(obsoletes)} fichier(s) supprime(s)")
    for f in obsoletes[:10]:
        print(f"      - {f}")

    # --- 3 bis. Purge des fichiers interdits physiquement presents -----------
    # Un fichier interdit peut exister sur le disque de la vitrine sans etre suivi
    # par git : .gitignore l'empeche d'etre pousse AUJOURD'HUI, mais une seule
    # modification de .gitignore suffirait a le publier. On ne le tolere pas.
    titre("3 bis. Purge des fichiers interdits")
    purges = []
    for racine, dossiers, noms in os.walk(VITRINE):
        dossiers[:] = [d for d in dossiers if d != ".git"]
        for nom in noms:
            chemin = os.path.join(racine, nom)
            rel = os.path.relpath(chemin, VITRINE).replace(os.sep, "/")
            if INTERDITS.search(rel):
                os.remove(chemin)
                purges.append(rel)
    print(f"   {len(purges)} fichier(s) purge(s)")
    for f in purges[:10]:
        print(f"      - {f}")

    # --- 4. Scan anti-fuite --------------------------------------------------
    titre("4. Scan anti-fuite")
    alertes = []
    for racine, dossiers, noms in os.walk(VITRINE):
        dossiers[:] = [d for d in dossiers if d != ".git"]
        for nom in noms:
            chemin = os.path.join(racine, nom)
            rel = os.path.relpath(chemin, VITRINE).replace(os.sep, "/")

            if INTERDITS.search(rel):
                alertes.append(("FICHIER INTERDIT", rel, 0))
                continue
            if os.path.splitext(nom)[1].lower() not in EXTENSIONS_SCANNEES:
                continue
            try:
                with open(chemin, encoding="utf-8", errors="ignore") as fh:
                    texte = fh.read()
            except OSError:
                continue
            for libelle, motif in MOTIFS:
                for m in motif.finditer(texte):
                    ligne = texte[: m.start()].count("\n") + 1
                    alertes.append((libelle, rel, ligne))

    if alertes:
        print(f"   {len(alertes)} point(s) a examiner (valeurs volontairement masquees) :")
        for libelle, fichier, ligne in alertes[:40]:
            emplacement = f"{fichier}:{ligne}" if ligne else fichier
            print(f"      [{libelle}] {emplacement}")
        if len(alertes) > 40:
            print(f"      ... et {len(alertes) - 40} autre(s)")
        print()
        print("   Ouvre chacun AVANT de commiter. Beaucoup sont de faux positifs")
        print("   (noms de proprietes, appels de methodes), mais aucun ne se")
        print("   presume inoffensif : la vitrine est publique.")
    else:
        print("   aucun secret detecte")

    # --- 5. Suite ------------------------------------------------------------
    titre("5. A faire maintenant")
    print(f"   cd {VITRINE}")
    print("   git status          # relis ce qui a change")
    print("   git add -A")
    print('   git commit -m "Synchronisation depuis le depot de travail"')
    print("   git push")
    print()
    print("   Le script ne commite ni ne pousse volontairement : une vitrine")
    print("   publique merite une relecture humaine avant chaque publication.")

    return 1 if alertes else 0


if __name__ == "__main__":
    sys.exit(main())
